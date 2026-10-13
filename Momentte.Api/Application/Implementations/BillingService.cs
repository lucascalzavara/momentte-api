using System.Data;
using System.Text.Json;
using Dapper;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Infrastructure.Implementations;
using Momentte.Api.Infrastructure.Interfaces;
using Momentte.Api.Application.Interfaces;
namespace Momentte.Api.Application.Implementations;
public sealed class BillingService(IBillingRepository repo, IDbConnectionFactory factory, IMercadoPagoGateway gateway, IConfiguration config) : IBillingService
{
    private string Product => config["Billing:Product"] ?? "momentte";
    private const string OrderSelect = "SELECT id AS Id, owner_id AS OwnerId, request_key AS RequestKey, kind AS Kind, plan_code AS PlanCode, previous_plan AS PreviousPlan, event_id AS EventId, cycle_id AS CycleId, amount_cents AS AmountCents, status AS Status, provider_id AS ProviderId, checkout_url AS CheckoutUrl, valid_until AS ValidUntil FROM billing_orders ";
    private const string ContractSelect = "SELECT id AS Id, owner_id AS OwnerId, plan_code AS PlanCode, pending_plan AS PendingPlan, provider_id AS ProviderId, checkout_url AS CheckoutUrl, cancelled AS Cancelled FROM billing_contracts ";
    public Task<BillingOverview> Overview(Guid owner, CancellationToken ct) => repo.Locked(owner, async (db, tx) =>
    {
        var cycle = await repo.Current(db, tx, owner);
        return new BillingOverview(BillingPolicy.Get(Product, cycle?.PlanCode ?? "free"), cycle, await repo.Contract(db, tx, owner),
            Product == "momentte" ? BillingPolicy.MomenttePlans : BillingPolicy.AlicercePlans,
            await db.QueryAsync<BillingOrder>(OrderSelect + "WHERE owner_id=@owner ORDER BY created_at DESC LIMIT 50", new { owner }, tx), gateway.Available);
    }, ct);
    public async Task<object> Checkout(Guid owner, string email, CheckoutRequest request, CancellationToken ct)
    {
    // Reserva durável antes da chamada externa: timeout/rollback não muda a cotação nem a referência.
    var reserved = await repo.Locked<object>(owner, async (db, tx) =>
    {
        if (request.RequestKey == Guid.Empty) throw new BillingRuleException(400, "Informe uma chave de idempotência.");
        if (!gateway.Available) throw new BillingRuleException(503, "Checkout indisponível até configurar o Mercado Pago.");
        var cycle = await repo.Current(db, tx, owner);
        var contract = await repo.Contract(db, tx, owner);
        if (request.Kind == "subscription")
        {
            var plan = BillingPolicy.Get(Product, request.PlanCode);
            if (plan.PriceCents == 0) throw new BillingRuleException(400, "O plano gratuito não exige pagamento.");
            var id = request.RequestKey;
            var prior = await db.QuerySingleOrDefaultAsync<BillingContract>(ContractSelect + "WHERE id=@id AND owner_id=@owner", new { id, owner }, tx);
            if (prior is not null) { if(prior.PlanCode != plan.Code) throw new BillingRuleException(409, "Chave reutilizada para outro plano."); return prior; }
            if (cycle is not null || contract is { Cancelled: false }) throw new BillingRuleException(409, "Já existe uma assinatura. Use a troca de plano ou o checkout pendente.");
            await db.ExecuteAsync("INSERT INTO billing_contracts(id,owner_id,plan_code) VALUES(@id,@owner,@code)", new { id, owner, code = plan.Code }, tx);
            return new BillingContract { Id = id, OwnerId = owner, PlanCode = plan.Code };
        }
        var old = await db.QuerySingleOrDefaultAsync<BillingOrder>(OrderSelect + "WHERE owner_id=@owner AND request_key=@key", new { owner, key = request.RequestKey }, tx);
        if (old is not null) { if(old.Kind != request.Kind || old.EventId != request.EventId && old.Kind == "event") throw new BillingRuleException(409, "Chave reutilizada para outra contratação."); return old; }
        var order = new BillingOrder { Id = request.RequestKey, OwnerId = owner, RequestKey = request.RequestKey, Kind = request.Kind, PlanCode = request.PlanCode };
        if (request.Kind == "event" && Product == "momentte")
        {
            var exists = await db.ExecuteScalarAsync<int>("SELECT count(*) FROM events WHERE id=@id AND owner_id=@owner AND activated_at IS NULL AND deleted_at IS NULL", new { id = request.EventId, owner }, tx);
            if (exists != 1) throw new BillingRuleException(409, "Compre o pacote antes de iniciar o evento.");
            if (await db.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_orders WHERE event_id=@id AND status IN ('pending','paid')", new { id = request.EventId }, tx) > 0)
                throw new BillingRuleException(409, "Este evento já possui uma compra paga ou pendente.");
            order.EventId = request.EventId; order.PlanCode = "paid-event";
            order.AmountCents = BillingPolicy.EventPrice(cycle is not null, cycle?.ActivationsUsed ?? 0);
        }
        else if (request.Kind == "upgrade" && Product == "alicerce" && cycle is not null && contract is { Cancelled: false, ProviderId: not null })
        {
            var plan = BillingPolicy.Get(Product, request.PlanCode);
            order.CycleId = cycle.Id; order.PreviousPlan = cycle.PlanCode; order.ValidUntil = cycle.EndsAt;
            order.AmountCents = BillingPolicy.Prorate(BillingPolicy.Get(Product, cycle.PlanCode).PriceCents, plan.PriceCents, cycle.StartsAt, cycle.EndsAt, DateTime.UtcNow);
            if (await db.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_orders WHERE owner_id=@owner AND kind='upgrade' AND status='pending' AND valid_until>CURRENT_TIMESTAMP", new { owner }, tx) > 0)
                throw new BillingRuleException(409, "Já há um upgrade aguardando pagamento.");
        }
        else throw new BillingRuleException(400, "Contratação inválida para este produto.");
        await db.ExecuteAsync("""
            INSERT INTO billing_orders(id,owner_id,request_key,kind,plan_code,previous_plan,event_id,cycle_id,amount_cents,provider_id,checkout_url,valid_until)
            VALUES(@Id,@OwnerId,@RequestKey,@Kind,@PlanCode,@PreviousPlan,@EventId,@CycleId,@AmountCents,@ProviderId,@CheckoutUrl,@ValidUntil)
            """, order, tx);
        return order;
    }, ct);
    return await repo.Locked<object>(owner, async (db, tx) =>
    {
        if (reserved is BillingContract attempt)
        {
            var current = (await db.QuerySingleAsync<BillingContract>(ContractSelect + "WHERE id=@id AND owner_id=@owner", new { id = attempt.Id, owner }, tx));
            if (current.Cancelled) throw new BillingRuleException(409, "A contratação foi cancelada.");
            if (current.CheckoutUrl is not null) return current;
            var plan = BillingPolicy.Get(Product, current.PlanCode);
            var checkout = await gateway.Checkout(current.Id, $"{Product} — {plan.Name}", plan.PriceCents, email, true, null, ct);
            await db.ExecuteAsync("UPDATE billing_contracts SET provider_id=@provider,checkout_url=@url WHERE id=@id", new { provider = checkout.Id, url = checkout.Url, id = current.Id }, tx);
            current.ProviderId = checkout.Id; current.CheckoutUrl = checkout.Url; return current;
        }
        var order = await db.QuerySingleAsync<BillingOrder>(OrderSelect + "WHERE id=@id AND owner_id=@owner", new { id = ((BillingOrder)reserved).Id, owner }, tx);
        if (order.Status != "pending") throw new BillingRuleException(409, "A compra já foi processada.");
        if (order.ValidUntil is not null && order.ValidUntil <= DateTime.UtcNow) throw new BillingRuleException(409, "A cotação expirou. Solicite uma nova contratação.");
        if (order.CheckoutUrl is not null) return order;
        var result = await gateway.Checkout(order.Id, order.Kind == "event" ? "Momentte — evento pago" : $"Alicerce — upgrade {order.PlanCode}", order.AmountCents, email, false, order.ValidUntil, ct);
        await db.ExecuteAsync("UPDATE billing_orders SET provider_id=@provider,checkout_url=@url WHERE id=@id", new { provider = result.Id, url = result.Url, id = order.Id }, tx);
        order.ProviderId = result.Id; order.CheckoutUrl = result.Url; return order;
    }, ct);
    }
    public async Task ChangePlan(Guid owner, string plan, CancellationToken ct)
    {
        await repo.Locked(owner, async (db, tx) =>
        {
            var target = BillingPolicy.Get(Product, plan);
            var cycle = await repo.Current(db, tx, owner);
            var contract = await repo.Contract(db, tx, owner);
            if (contract is null) throw new BillingRuleException(409, "Não há assinatura para alterar.");
            if (contract.ProviderId is null)
            {
                if (target.Code != "free") throw new BillingRuleException(409, "Conclua ou cancele o checkout pendente antes de trocar o plano.");
                var reference = Product + ":" + contract.Id;
                var search = await gateway.Send(HttpMethod.Get, "preapproval/search?external_reference=" + Uri.EscapeDataString(reference), ct: ct);
                if (search.TryGetProperty("results", out var attempts)) foreach (var attempt in attempts.EnumerateArray())
                    if (attempt.Text("external_reference") == reference && attempt.Text("status") != "cancelled")
                        await gateway.Send(HttpMethod.Put, "preapproval/" + Uri.EscapeDataString(attempt.Text("id")), new { status = "cancelled" }, ct: ct);
                await db.ExecuteAsync("UPDATE billing_contracts SET cancelled=TRUE,pending_plan='free' WHERE id=@id", new { id = contract.Id }, tx);
                return true;
            }
            if (target.PriceCents > BillingPolicy.Get(Product, cycle?.PlanCode ?? "free").PriceCents) throw new BillingRuleException(409, "Use o checkout de upgrade para pagar a diferença.");
            if (target.Code == "free")
                await gateway.Send(HttpMethod.Put, "preapproval/" + Uri.EscapeDataString(contract.ProviderId), new { status = "cancelled" }, ct: ct);
            else
            {
                if (contract.Cancelled) throw new BillingRuleException(409, "A assinatura foi cancelada. Aguarde o término para contratar novamente.");
                await gateway.Send(HttpMethod.Put, "preapproval/" + Uri.EscapeDataString(contract.ProviderId), new { auto_recurring = new { transaction_amount = target.PriceCents / 100m, currency_id = "BRL" } }, ct: ct);
            }
            await db.ExecuteAsync("UPDATE billing_contracts SET pending_plan=@plan,cancelled=@cancel WHERE id=@id", new { plan, cancel = plan == "free", id = contract.Id }, tx);
            // Nenhum ciclo pago é alterado: downgrade só se aplica ao próximo pagamento.
            return true;
        }, ct);
    }
    public async Task Notify(string topic, string id, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 100 || !id.All(char.IsLetterOrDigit)) throw new BillingRuleException(400, "Identificador inválido.");
        if (topic == "payment") await SettlePayment(await gateway.Send(HttpMethod.Get, "v1/payments/" + id, ct: ct), null, null, ct);
        else if (topic == "subscription_authorized_payment")
        {
            var invoice = await gateway.Send(HttpMethod.Get, "authorized_payments/" + id, ct: ct);
            if (invoice.TryGetProperty("payment", out var nested) && nested.Text("status") == "approved")
                await SettlePayment(await gateway.Send(HttpMethod.Get, "v1/payments/" + nested.Text("id"), ct: ct), invoice.Text("preapproval_id"), invoice.Utc("debit_date"), ct);
        }
        else if (topic == "subscription_preapproval")
        {
            var subscription = await gateway.Send(HttpMethod.Get, "preapproval/" + id, ct: ct);
            if (subscription.Text("status") is "cancelled" or "paused")
            {
                using var db = factory.CreateConnection();
                await db.ExecuteAsync("UPDATE billing_contracts SET cancelled=TRUE WHERE provider_id=@id", new { id });
            }
        }
    }
    public async Task Reconcile(Guid owner, CancellationToken ct)
    {
        if (!gateway.Available) return;
        var view = await Overview(owner, ct);
        foreach (var order in view.Orders.Where(o => o.Status == "pending"))
        {
            var result = await gateway.Send(HttpMethod.Get, "v1/payments/search?external_reference=" + Uri.EscapeDataString(Product + ":" + order.Id), ct: ct);
            if (result.TryGetProperty("results", out var payments)) foreach (var payment in payments.EnumerateArray()) await SettlePayment(payment, null, null, ct);
        }
        if (view.Contract?.ProviderId is not null)
        {
            var result = await gateway.Send(HttpMethod.Get, "authorized_payments/search?preapproval_id=" + Uri.EscapeDataString(view.Contract.ProviderId), ct: ct);
            if (result.TryGetProperty("results", out var invoices)) foreach (var invoice in invoices.EnumerateArray().OrderBy(i => i.Text("debit_date"))) await Notify("subscription_authorized_payment", invoice.Text("id"), ct);
        }
    }
    private async Task SettlePayment(JsonElement payment, string? providerContract, DateTime? debitDate, CancellationToken ct)
    {
        if (payment.Text("currency_id") != "BRL") return;
        if (!string.IsNullOrEmpty(config["MercadoPago:CollectorId"]) && payment.Text("collector_id") != config["MercadoPago:CollectorId"])
            throw new BillingRuleException(400, "Recebedor do pagamento divergente.");
        var paymentId = payment.Text("id");
        using var read = factory.CreateConnection();
        BillingOrder? order = null; BillingContract? contract = null;
        if (providerContract is not null) contract = await read.QuerySingleOrDefaultAsync<BillingContract>(ContractSelect + "WHERE provider_id=@providerContract", new { providerContract });
        else
        {
            var external = payment.Text("external_reference");
            if (external.StartsWith(Product + ":", StringComparison.Ordinal) && Guid.TryParse(external[(Product.Length + 1)..], out var reference))
                order = await read.QuerySingleOrDefaultAsync<BillingOrder>(OrderSelect + "WHERE id=@reference", new { reference });
        }
        if (order is null && contract is null) return;
        var owner = order?.OwnerId ?? contract!.OwnerId;
        await repo.Locked(owner, async (db, tx) =>
        {
            var status = payment.Text("status");
            if (status is "refunded" or "charged_back")
            {
                await db.ExecuteAsync("UPDATE billing_payments SET status=@status WHERE provider_payment_id=@paymentId", new { status, paymentId }, tx);
                if (order is not null) await db.ExecuteAsync("UPDATE billing_orders SET status=@status WHERE id=@id", new { status, id = order.Id }, tx);
                return true;
            }
            if (status != "approved") return true;
            if (await db.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_payments WHERE provider_payment_id=@paymentId", new { paymentId }, tx) > 0) return true;
            var amount = payment.Cents("transaction_amount");
            var approved = payment.Utc("date_approved");
            if (order is not null)
            {
                if (amount != order.AmountCents) throw new BillingRuleException(400, "Valor do pagamento divergente.");
                var liveStatus = await db.QuerySingleAsync<string>("SELECT status FROM billing_orders WHERE id=@id", new { id = order.Id }, tx);
                if (liveStatus != "pending")
                {
                    await db.ExecuteAsync("INSERT INTO billing_payments(provider_payment_id,owner_id,order_id,amount_cents,approved_at,status) VALUES(@paymentId,@owner,@id,@amount,@approved,'review_required')", new { paymentId, owner, id = order.Id, amount, approved }, tx);
                    return true;
                }
                var current = await repo.Current(db, tx, owner);
                var state = "paid";
                if (order.Kind == "upgrade")
                {
                    var activeContract = await repo.Contract(db, tx, owner);
                    if (current is null || current.Id != order.CycleId || current.PlanCode != order.PreviousPlan || order.ValidUntil <= DateTime.UtcNow || activeContract is not { Cancelled: false, ProviderId: not null }) state = "review_required";
                    else
                    {
                        var target = BillingPolicy.Get(Product, order.PlanCode);
                        await gateway.Send(HttpMethod.Put, "preapproval/" + Uri.EscapeDataString(activeContract.ProviderId), new { auto_recurring = new { transaction_amount = target.PriceCents / 100m, currency_id = "BRL" } }, ct: ct);
                        await db.ExecuteAsync("UPDATE billing_cycles SET plan_code=@code WHERE id=@id; UPDATE billing_contracts SET plan_code=@code,pending_plan=NULL WHERE id=@contract", new { code = target.Code, id = current.Id, contract = activeContract.Id }, tx);
                    }
                }
                await db.ExecuteAsync("UPDATE billing_orders SET status=@state WHERE id=@id", new { state, id = order.Id }, tx);
            }
            else
            {
                // O valor efetivamente cobrado identifica o plano do ciclo, inclusive durante downgrade.
                var plan = (Product == "momentte" ? BillingPolicy.MomenttePlans : BillingPolicy.AlicercePlans).SingleOrDefault(p => p.PriceCents == amount && amount > 0)
                    ?? throw new BillingRuleException(400, "Valor da assinatura divergente.");
                var latestEnd = await db.QuerySingleOrDefaultAsync<DateTime?>("SELECT MAX(ends_at) FROM billing_cycles WHERE contract_id=@id", new { id = contract!.Id }, tx);
                var start = debitDate ?? approved;
                if (latestEnd is not null && start < latestEnd.Value) start = latestEnd.Value;
                await db.ExecuteAsync("INSERT INTO billing_cycles(id,owner_id,contract_id,plan_code,starts_at,ends_at) VALUES(@id,@owner,@contract,@code,@start,@end)", new { id = Guid.NewGuid(), owner, contract = contract.Id, code = plan.Code, start, end = start.AddMonths(1) }, tx);
                await db.ExecuteAsync("UPDATE billing_contracts SET plan_code=@code,pending_plan=CASE WHEN pending_plan=@code THEN NULL ELSE pending_plan END WHERE id=@id", new { code = plan.Code, id = contract.Id }, tx);
            }
            await db.ExecuteAsync("INSERT INTO billing_payments(provider_payment_id,owner_id,order_id,contract_id,amount_cents,approved_at,status) VALUES(@paymentId,@owner,@order,@contract,@amount,@approved,'approved')", new { paymentId, owner, order = order?.Id, contract = contract?.Id, amount, approved }, tx);
            return true;
        }, ct);
    }
}

