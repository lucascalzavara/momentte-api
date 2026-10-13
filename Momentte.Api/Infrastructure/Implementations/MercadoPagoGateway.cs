using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Infrastructure.Interfaces;
namespace Momentte.Api.Infrastructure.Implementations;
public sealed class MercadoPagoGateway(HttpClient http, IConfiguration config) : IMercadoPagoGateway
{
    public bool Available => !string.IsNullOrWhiteSpace(config["MercadoPago:AccessToken"]) &&
        Uri.TryCreate(config["MercadoPago:PublicApiUrl"], UriKind.Absolute, out var api) && api.Scheme == "https" &&
        Uri.TryCreate(config["MercadoPago:WebUrl"], UriKind.Absolute, out var web) && web.Scheme == "https";
    public async Task<JsonElement> Send(HttpMethod method, string path, object? body = null, string? idempotencyKey = null, CancellationToken ct = default)
    {
        if (!Available) throw new BillingRuleException(503, "Configure as credenciais e URLs HTTPS do Mercado Pago para habilitar o checkout.");
        using var request = new HttpRequestMessage(method, new Uri(new Uri("https://api.mercadopago.com/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["MercadoPago:AccessToken"]);
        if (idempotencyKey is not null) request.Headers.Add("X-Idempotency-Key", idempotencyKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new BillingRuleException(502, "O Mercado Pago não concluiu a solicitação. Tente novamente ou confira a configuração da integração.");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return json.Clone();
    }
    public async Task<(string Id, string Url)> Checkout(Guid id, string title, int cents, string email, bool subscription, DateTime? expires, CancellationToken ct)
    {
        var reference = (config["Billing:Product"] ?? "momentte") + ":" + id;
        var back = config["MercadoPago:WebUrl"]!.TrimEnd('/') + (config["Billing:Product"] == "alicerce" ? "/app" : "/conta");
        var hook = config["MercadoPago:PublicApiUrl"]!.TrimEnd('/') + "/api/v1/billing/mercadopago/webhook";
        JsonElement result;
        if (subscription)
        {
            // Recupera tentativa anterior antes de criar uma segunda assinatura.
            var existing = await Send(HttpMethod.Get, "preapproval/search?external_reference=" + Uri.EscapeDataString(reference), ct: ct);
            if (existing.TryGetProperty("results", out var list))
                foreach (var item in list.EnumerateArray())
                    if (item.Text("external_reference") == reference && item.Text("status") != "cancelled")
                        return (item.Text("id"), item.Text("init_point"));
            result = await Send(HttpMethod.Post, "preapproval", new {
                reason = title, external_reference = reference, payer_email = email, status = "pending",
                auto_recurring = new { frequency = 1, frequency_type = "months", transaction_amount = cents / 100m, currency_id = "BRL" },
                back_url = back, notification_url = hook,
            }, reference, ct);
        }
        else
        {
            var search = await Send(HttpMethod.Get, "checkout/preferences/search?external_reference=" + Uri.EscapeDataString(reference), ct: ct);
            if (search.TryGetProperty("elements", out var preferences))
                foreach (var preference in preferences.EnumerateArray())
                {
                    var found = await Send(HttpMethod.Get, "checkout/preferences/" + Uri.EscapeDataString(preference.Text("id")), ct: ct);
                    if (found.Text("external_reference") == reference)
                    {
                        var recovered = found.Text(config.GetValue<bool>("MercadoPago:Sandbox") ? "sandbox_init_point" : "init_point");
                        if (recovered.StartsWith("https://", StringComparison.Ordinal)) return (found.Text("id"), recovered);
                    }
                }
            result = await Send(HttpMethod.Post, "checkout/preferences", new {
                items = new[] { new { id = reference, title, quantity = 1, currency_id = "BRL", unit_price = cents / 100m } },
                external_reference = reference, payer = new { email }, notification_url = hook,
                back_urls = new { success = back, pending = back, failure = back }, auto_return = "approved",
                expires = expires is not null, expiration_date_to = expires?.ToString("O"),
            }, reference, ct);
        }
        var url = result.Text(config.GetValue<bool>("MercadoPago:Sandbox") && !subscription ? "sandbox_init_point" : "init_point");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) || target.Scheme != "https") throw new BillingRuleException(502, "O checkout retornado não é válido.");
        return (result.Text("id"), url);
    }
    public bool VerifySignature(string dataId, string requestId, string signature)
    {
        var secret = config["MercadoPago:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrEmpty(requestId) || string.IsNullOrEmpty(dataId)) return false;
        var parts = signature.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToArray();
        var ts = parts.FirstOrDefault(p => p[0].Trim() == "ts")?[1].Trim();
        var digest = parts.FirstOrDefault(p => p[0].Trim() == "v1")?[1].Trim();
        if (ts is null || digest is null) return false;
        var manifest = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{ts};";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest));
        try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(digest)); } catch (FormatException) { return false; }
    }
}
public static class MercadoPagoJson
{
    public static string Text(this JsonElement json, string property) => json.TryGetProperty(property, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString() : "";
    public static int Cents(this JsonElement json, string property) => decimal.TryParse(json.Text(property), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount) ? checked((int)Math.Round(amount * 100, MidpointRounding.AwayFromZero)) : -1;
    public static DateTime Utc(this JsonElement json, string property) => DateTimeOffset.TryParse(json.Text(property), out var time) ? time.UtcDateTime : throw new BillingRuleException(502, "Pagamento sem data válida.");
}
