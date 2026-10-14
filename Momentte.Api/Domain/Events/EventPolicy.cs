using Momentte.Api.Domain.Billing;
namespace Momentte.Api.Domain.Events;
public static class EventPolicy
{
    public static readonly string[] Fonts = ["sans", "serif", "rounded"];
    public static readonly string[] Scales = ["small", "medium", "large"];
    public static void ValidateSchedule(DateTime start, DateTime end, DateTime now)
    {
        if (end<=start || end-start>TimeSpan.FromHours(48)) throw new BillingRuleException(400,"O evento deve durar mais de zero e no máximo 48 horas.");
        if (end<=now) throw new BillingRuleException(400,"O término deve estar no futuro.");
    }
    public static void ValidateActivation(EventRecord item, DateTime now)
    {
        if (now<item.StartsAt) throw new BillingRuleException(409,"Aguarde o início previsto para ativar o evento.");
        if (item.EndsAt<=now || item.EndsAt-now>TimeSpan.FromHours(48)) throw new BillingRuleException(409,"O término precisa estar no futuro e a no máximo 48 horas do início efetivo.");
    }
    public static void ValidateContent(string mode, bool image, string? text)
    {
        if (!image && string.IsNullOrWhiteSpace(text)) throw new BillingRuleException(400,"Envie uma imagem, um texto ou ambos.");
        if (text?.Length>500) throw new BillingRuleException(400,"O texto pode ter até 500 caracteres.");
        if(text?.Contains('\0')==true)throw new BillingRuleException(400,"O texto contém um caractere inválido.");
        if (mode=="images" && !string.IsNullOrWhiteSpace(text) || mode=="texts" && image) throw new BillingRuleException(400,"Este conteúdo não é permitido pela configuração do evento.");
    }
    public static bool DownloadAvailable(DateTime? activated, DateTime end, int hours, DateTime now) => activated is not null && now>=end && now<end.AddHours(hours);
}
