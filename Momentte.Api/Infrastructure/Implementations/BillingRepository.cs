using System.Data;
using System.Data.Common;
using Dapper;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Infrastructure.Implementations;
public sealed class BillingRepository(IDbConnectionFactory factory) : IBillingRepository
{
    public async Task<T> Locked<T>(Guid owner, Func<IDbConnection, IDbTransaction, Task<T>> action, CancellationToken ct = default)
    {
        using var db = factory.CreateConnection();
        await ((DbConnection)db).OpenAsync(ct);
        using var tx = db.BeginTransaction();
        await db.ExecuteAsync("INSERT INTO billing_accounts(owner_id) VALUES(@owner) ON CONFLICT DO NOTHING", new { owner }, tx);
        await db.ExecuteScalarAsync<Guid>("SELECT owner_id FROM billing_accounts WHERE owner_id=@owner FOR UPDATE", new { owner }, tx);
        var result = await action(db, tx);
        tx.Commit();
        return result;
    }
    public Task<BillingCycle?> Current(IDbConnection db, IDbTransaction tx, Guid owner) => db.QuerySingleOrDefaultAsync<BillingCycle>("""
        SELECT id AS Id, owner_id AS OwnerId, contract_id AS ContractId, plan_code AS PlanCode, starts_at AS StartsAt,
               ends_at AS EndsAt, activations_used AS ActivationsUsed
        FROM billing_cycles WHERE owner_id=@owner AND starts_at<=clock_timestamp() AND ends_at>clock_timestamp()
        ORDER BY starts_at DESC LIMIT 1
        """, new { owner }, tx);
    public Task<BillingContract?> Contract(IDbConnection db, IDbTransaction tx, Guid owner) => db.QuerySingleOrDefaultAsync<BillingContract>("""
        SELECT id AS Id, owner_id AS OwnerId, plan_code AS PlanCode, pending_plan AS PendingPlan, provider_id AS ProviderId,
               checkout_url AS CheckoutUrl, cancelled AS Cancelled FROM billing_contracts
        WHERE owner_id=@owner ORDER BY created_at DESC LIMIT 1
        """, new { owner }, tx);
}
