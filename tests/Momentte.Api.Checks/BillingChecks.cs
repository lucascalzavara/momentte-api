using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Infrastructure.Implementations;

public static class BillingChecks
{
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("OK: " + label); }
    public static async Task Run()
    {
        Check(BillingPolicy.Get("momentte", "professional").PriceCents == 9900 && BillingPolicy.Get("momentte", "professional").EventActivations == 8, "Assinatura Momentte: R$99 e 8 ativações");
        Check(BillingPolicy.EventPrice(false, 8) == 3000 && BillingPolicy.EventPrice(true, 7) == 3000 && BillingPolicy.EventPrice(true, 8) == 2000, "Avulso R$30 e excedente R$20 somente após franquia");
        Check(BillingPolicy.FreeEventBytes == 1_000_000_000 && BillingPolicy.PaidEventBytes == 10_000_000_000, "Limites em GB decimais");
        foreach (var (code, limit, amount) in new[] { ("free", 2, 0), ("starter", 5, 1500), ("business", 20, 3000) })
        {
            var plan = BillingPolicy.Get("alicerce", code);
            Check(plan.ActiveWorks == limit && plan.PriceCents == amount && BillingPolicy.CanActivateWork(limit - 1, plan.ActiveWorks) && !BillingPolicy.CanActivateWork(limit, plan.ActiveWorks), "Limite exato e preço Alicerce: " + code);
        }
        Check(BillingPolicy.CanActivateWork(500, BillingPolicy.Get("alicerce", "unlimited").ActiveWorks) && BillingPolicy.Get("alicerce", "unlimited").PriceCents == 9900, "Plano ilimitado R$99");
        Check(!BillingPolicy.CanActivateWork(20, 5), "Downgrade acima do limite impede novas ativações");
        Check(!BillingPolicy.Get("alicerce", "free").CsvExport && BillingPolicy.AlicercePlans.Where(p => p.Code != "free").All(p => p.CsvExport), "CSV somente nos planos pagos");
        var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc); var end = start.AddDays(30);
        Check(BillingPolicy.Prorate(1500, 3000, start, end, start.AddDays(15)) == 750, "Upgrade na metade do ciclo cobra R$7,50");
        Check(BillingPolicy.EventState(null, end, 24, start) == "organization", "Rascunho permanece em organização");
        Check(BillingPolicy.EventState(start, end, 24, end.AddTicks(-1)) == "active", "Recebimento encerra exatamente no término");
        Check(BillingPolicy.EventState(start, end, 24, end) == "awaiting_download", "Início da janela de download");
        Check(BillingPolicy.EventState(start, end, 24, end.AddHours(24)) == "finalized" && BillingPolicy.EventState(start, end, 72, end.AddHours(24)) == "awaiting_download" && BillingPolicy.EventState(start, end, 72, end.AddHours(72)) == "finalized", "Fronteiras exatas de 24h e 72h");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["MercadoPago:AccessToken"] = "test-token", ["MercadoPago:WebhookSecret"] = "test-secret", ["MercadoPago:PublicApiUrl"] = "https://api.example.com", ["MercadoPago:WebUrl"] = "https://web.example.com",
        }).Build();
        var gateway = new MercadoPagoGateway(new HttpClient(new StubHandler()), config);
        const string manifest = "id:abc123;request-id:request;ts:123;";
        var hash = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), Encoding.UTF8.GetBytes(manifest)));
        Check(gateway.VerifySignature("ABC123", "request", "ts=123,v1=" + hash), "Assinatura Mercado Pago e normalização do ID");
        Check(!gateway.VerifySignature("ABC124", "request", "ts=123,v1=" + hash) && !gateway.VerifySignature("ABC123", "other-request", "ts=123,v1=" + hash) && !gateway.VerifySignature("ABC123", "request", "ts=123,v1=invalid"), "Webhook adulterado rejeitado");
        var checkout = await gateway.Checkout(Guid.NewGuid(), "Evento", 2000, "test@example.com", false, null, default);
        Check(checkout.Url.StartsWith("https://"), "Checkout usa preço do servidor, BRL e referência local");
        var subscription = await gateway.Checkout(Guid.NewGuid(), "Plano", 9900, "test@example.com", true, null, default);
        Check(subscription.Url.StartsWith("https://"), "Assinatura mensal pendente não equivale a pagamento aprovado");

        var root = Path.Combine(Path.GetTempPath(), "momentte-media-check-" + Guid.NewGuid().ToString("N"));
        config["Media:RootPath"] = root;
        var store = new LocalMediaStore(new FakeEnvironment(), config);
        var jpeg = new byte[] { 255, 216, 255, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var saved = await store.Put(new MemoryStream(jpeg), Guid.NewGuid(), jpeg.Length, default);
        Check(saved.Bytes == jpeg.Length && saved.ContentType == "image/jpeg", "Upload mede bytes reais e identifica formato");
        try { await store.Put(new MemoryStream(jpeg), Guid.NewGuid(), jpeg.Length - 1, default); throw new Exception("Quota não aplicada"); } catch (BillingRuleException e) when (e.Status == 409) { }
        Check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 1, "Upload acima da quota não deixa arquivo parcial");
        try { store.Read("../outside"); throw new Exception("Path traversal aceito"); } catch (BillingRuleException e) when (e.Status == 400) { }
        await store.Delete(saved.Path);
        var resolvedRoot = Path.GetFullPath(root);
        if (!resolvedRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolvedRoot).StartsWith("momentte-media-check-", StringComparison.Ordinal)) throw new Exception("Pasta temporária inválida");
        Directory.Delete(resolvedRoot, true); // Somente a pasta temporária criada neste teste.
        Console.WriteLine("Verificações de planos/mídia/Mercado Pago concluídas, sem chamadas externas.");
    }
    private sealed class StubHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Parameter != "test-token") throw new Exception("Token não enviado");
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[],\"elements\":[]}") };
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var body = json.RootElement;
            if (!body.GetProperty("external_reference").GetString()!.StartsWith("momentte:", StringComparison.Ordinal)) throw new Exception("Referência não isolada por produto");
            if (path == "/checkout/preferences")
            {
                var item = body.GetProperty("items")[0];
                if (item.GetProperty("unit_price").GetDecimal() != 20m || item.GetProperty("currency_id").GetString() != "BRL") throw new Exception("Preço divergente");
            }
            else if (body.GetProperty("status").GetString() != "pending" || body.GetProperty("auto_recurring").GetProperty("frequency_type").GetString() != "months" || body.GetProperty("auto_recurring").GetProperty("transaction_amount").GetDecimal() != 99m) throw new Exception("Recorrência inválida");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"test\",\"init_point\":\"https://www.mercadopago.com.br/checkout/test\"}") };
        }
    }
    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "test"; public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Path.GetTempPath(); public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = ""; public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
