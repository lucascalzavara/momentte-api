using System.Text.Json;
using Momentte.Api.Domain.Events;
using Momentte.Api.Domain.Billing;
public static class EventChecks
{
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("OK: "+name);}
    private static void Reject(Action action,string name){try{action();}catch(BillingRuleException){Console.WriteLine("OK: "+name);return;}throw new Exception(name);}
    public static void Run()
    {
        var now=new DateTime(2026,10,13,12,0,0,DateTimeKind.Utc);
        EventPolicy.ValidateSchedule(now,now.AddHours(48),now.AddSeconds(-1));
        Reject(()=>EventPolicy.ValidateSchedule(now,now.AddHours(48).AddSeconds(1),now),"Duração maior que 48h rejeitada");
        Reject(()=>EventPolicy.ValidateSchedule(now,now,now),"Intervalo vazio rejeitado");
        var item=new EventRecord{StartsAt=now.AddHours(1),EndsAt=now.AddHours(4)};
        Reject(()=>EventPolicy.ValidateActivation(item,now),"Não inicia antes do horário previsto");
        EventPolicy.ValidateActivation(item,now.AddHours(1));
        Reject(()=>EventPolicy.ValidateActivation(item,now.AddHours(4)),"Não inicia no término ou depois dele");
        foreach(var(image,text)in new[]{(true,(string?)null),(false,"Uma mensagem"),(true,"Uma legenda")})EventPolicy.ValidateContent("both",image,text);
        EventPolicy.ValidateContent("texts",false,new string('a',500));
        Reject(()=>EventPolicy.ValidateContent("both",false,"  "),"Envio vazio rejeitado");
        Reject(()=>EventPolicy.ValidateContent("images",true,"legenda"),"Modo imagens proíbe texto");
        Reject(()=>EventPolicy.ValidateContent("texts",true,null),"Modo textos proíbe imagem");
        Reject(()=>EventPolicy.ValidateContent("texts",false,new string('a',501)),"Texto acima de 500 caracteres rejeitado");
        Reject(()=>EventPolicy.ValidateContent("texts",false,"texto\0"),"Caractere incompatível com o banco rejeitado");
        Check(!EventPolicy.DownloadAvailable(now,now.AddHours(4),24,now),"Sem download durante evento ativo");
        Check(EventPolicy.DownloadAvailable(now,now.AddHours(4),24,now.AddHours(4)),"Download liberado no encerramento");
        Check(!EventPolicy.DownloadAvailable(now,now.AddHours(4),24,now.AddHours(28))&&EventPolicy.DownloadAvailable(now,now.AddHours(4),72,now.AddHours(28)),"Prazos distintos gratuitos/pagos");
        var draft=new EventRecord{Name="Evento",ThemeColor="#123456",TextColor="#ffffff",FontFamily="serif",TextScale="large",CustomLogoPath="private/image",Paid=false};
        Check(draft.ThemeColor=="#123456"&&draft.EffectiveBackground=="#2f241e"&&draft.EffectiveFont=="sans"&&draft.UsesMomentteBrand,"Gratuito preserva configuração mas aplica aparência padrão");
        draft.Paid=true;
        Check(draft.EffectiveBackground=="#123456"&&draft.EffectiveFont=="serif"&&!draft.UsesMomentteBrand,"Pago aplica aparência configurada");
        Check(!JsonSerializer.Serialize(draft).Contains("private/image"),"Caminho privado da logo não aparece no JSON");
        var media=new MediaRecord{RelativePath="private/photo",Text="<script>texto</script>"};
        Check(media.HasImage&&!JsonSerializer.Serialize(media).Contains("private/photo"),"Envio com legenda não expõe caminho privado");
        var ended=new EventRecord{EndsAt=now.AddHours(4),EndedAt=now.AddHours(3)};
        Check(ended.EffectiveEnd==now.AddHours(3),"Encerramento antecipado reduz prazo");
        ended.EndedAt=now.AddHours(5);
        Check(ended.EffectiveEnd==now.AddHours(4),"Encerramento nunca prorroga o horário previsto");
        Console.WriteLine("Regras de eventos, participação e aparência verificadas.");
    }
}
