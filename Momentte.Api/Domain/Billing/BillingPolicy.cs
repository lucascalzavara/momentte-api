namespace Momentte.Api.Domain.Billing;

public sealed record PlanDefinition(string Code, string Name, int PriceCents, int? ActiveWorks, bool CsvExport, int EventActivations);
public static class BillingPolicy
{
    public const long FreeEventBytes = 1_000_000_000;
    public const long PaidEventBytes = 10_000_000_000;
    public static readonly PlanDefinition[] MomenttePlans = [new("free", "Gratuito", 0, null, false, 0), new("professional", "Profissional", 9900, null, false, 8)];
    public static readonly PlanDefinition[] AlicercePlans = [new("free", "Gratuito", 0, 2, false, 0), new("starter", "Inicial", 1500, 5, true, 0), new("business", "Intermediário", 3000, 20, true, 0), new("unlimited", "Completo", 9900, null, true, 0)];
    public static PlanDefinition Get(string product, string code) => (product == "momentte" ? MomenttePlans : AlicercePlans).SingleOrDefault(p => p.Code == code)
        ?? throw new BillingRuleException(400, "Plano inválido.");
    public static int EventPrice(bool activeSubscription, int used) => activeSubscription && used >= 8 ? 2000 : 3000;
    public static int Prorate(int oldPrice, int newPrice, DateTime start, DateTime end, DateTime now)
    {
        if (end <= start || now >= end || newPrice <= oldPrice) throw new BillingRuleException(409, "O período ou upgrade não é válido.");
        return Math.Max(1, (int)Math.Round((newPrice - oldPrice) * Math.Clamp((end - now).TotalSeconds / (end - start).TotalSeconds, 0, 1), MidpointRounding.AwayFromZero));
    }
    public static bool CanActivateWork(int active, int? limit) => limit is null || active < limit;
    public static string EventState(DateTime? activated, DateTime end, int downloadHours, DateTime now)
        => activated is null ? "organization" : now < end ? "active" : now < end.AddHours(downloadHours) ? "awaiting_download" : "finalized";
}
public sealed class BillingRuleException(int status, string message) : Exception(message) { public int Status { get; } = status; }
