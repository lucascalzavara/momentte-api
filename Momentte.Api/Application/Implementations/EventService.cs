using System.Data;
using Dapper;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Domain.Events;
using Momentte.Api.Infrastructure.Interfaces;
using Momentte.Api.Application.Interfaces;
namespace Momentte.Api.Application.Implementations;
public sealed class EventService(IBillingRepository repo, IDbConnectionFactory factory, IMediaStore store) : IEventService
{
    private const string Select = "SELECT id AS Id,owner_id AS OwnerId,name AS Name,starts_at AS StartsAt,ends_at AS EndsAt,public_code AS PublicCode,media_mode AS MediaMode,moderation_enabled AS ModerationEnabled,show_qr_code AS ShowQrCode,text_color AS TextColor,font_family AS FontFamily,text_scale AS TextScale,activated_at AS ActivatedAt,ended_at AS EndedAt,paid AS Paid,activation_source AS ActivationSource,storage_limit_bytes AS StorageLimitBytes,storage_used_bytes AS StorageUsedBytes,download_hours AS DownloadHours,custom_logo_path AS CustomLogoPath,custom_logo_type AS CustomLogoType,custom_logo_bytes AS CustomLogoBytes,theme_color AS ThemeColor FROM events ";
    private const string MediaSelect = "SELECT m.id AS Id,m.event_id AS EventId,m.relative_path AS RelativePath,m.content_type AS ContentType,m.bytes AS Bytes,m.text_content AS Text,m.moderation_status AS Status,m.created_at AS CreatedAt,CASE WHEN @names OR m.display_author_name THEN u.name ELSE NULL END AS AuthorName FROM event_media m LEFT JOIN users u ON u.id=m.author_id ";
    private Task<EventRecord?> Find(IDbConnection db,IDbTransaction? tx,Guid? owner,Guid? id,string? code=null) => db.QuerySingleOrDefaultAsync<EventRecord>(Select+"WHERE deleted_at IS NULL AND (public_code=@code OR (id=@id AND owner_id=@owner))",new{owner,id,code},tx);
    private async Task<EventRecord> Get(IDbConnection db,IDbTransaction? tx,Guid owner,Guid id) => await Find(db,tx,owner,id) ?? throw new BillingRuleException(404,"Evento não encontrado.");
    private async Task<EventRecord> GetCode(IDbConnection db,IDbTransaction? tx,string code) => await Find(db,tx,null,null,code) ?? throw new BillingRuleException(404,"Convite não encontrado.");
    private static PublicEventInfo Public(EventRecord e) => new(e.Name,e.PublicCode,e.StartsAt,e.EffectiveEnd,e.State,e.MediaMode,e.ModerationEnabled,e.EffectiveBackground,e.EffectiveTextColor,e.EffectiveFont,e.EffectiveScale,e.Paid&&e.HasCustomLogo);
    private async Task Access(IDbConnection db,IDbTransaction? tx,Guid user,EventRecord item)
    {
        if(item.OwnerId!=user && await db.ExecuteScalarAsync<int>("SELECT count(*) FROM event_participants WHERE event_id=@id AND user_id=@user",new{id=item.Id,user},tx)==0) throw new BillingRuleException(403,"Entre neste evento antes de enviar conteúdo.");
    }
    public async Task<IEnumerable<EventRecord>> List(Guid owner,CancellationToken ct)
    {using var db=factory.CreateConnection();return await db.QueryAsync<EventRecord>(Select+"WHERE owner_id=@owner AND deleted_at IS NULL ORDER BY created_at DESC",new{owner});}
    public Task<EventRecord> Save(Guid owner,Guid? id,EventRequest r,CancellationToken ct) => repo.Locked(owner,async(db,tx)=>
    {
        EventPolicy.ValidateSchedule(r.StartsAt.UtcDateTime,r.EndsAt.UtcDateTime,DateTime.UtcNow);
        if(string.IsNullOrWhiteSpace(r.Name)||r.Name.Trim().Length>100) throw new BillingRuleException(400,"Informe um nome de até 100 caracteres.");
        if(!EventPolicy.Fonts.Contains(r.FontFamily)||!EventPolicy.Scales.Contains(r.TextScale)) throw new BillingRuleException(400,"Fonte ou escala inválida.");
        var key=id??Guid.NewGuid();
        if(id is not null && (await Get(db,tx,owner,key)).ActivatedAt is not null) throw new BillingRuleException(409,"A configuração está travada após iniciar o evento.");
        var args=new{key,owner,name=r.Name.Trim(),start=r.StartsAt.UtcDateTime,end=r.EndsAt.UtcDateTime,mode=r.MediaMode,moderation=r.ModerationEnabled,showQr=r.ShowQrCode,background=r.ThemeColor,text=r.TextColor,font=r.FontFamily,scale=r.TextScale,code=Guid.NewGuid().ToString("N")};
        if(id is null) await db.ExecuteAsync("INSERT INTO events(id,owner_id,name,starts_at,ends_at,public_code,media_mode,moderation_enabled,show_qr_code,theme_color,text_color,font_family,text_scale) VALUES(@key,@owner,@name,@start,@end,@code,@mode,@moderation,@showQr,@background,@text,@font,@scale)",args,tx);
        else await db.ExecuteAsync("UPDATE events SET name=@name,starts_at=@start,ends_at=@end,media_mode=@mode,moderation_enabled=@moderation,show_qr_code=@showQr,theme_color=@background,text_color=@text,font_family=@font,text_scale=@scale WHERE id=@key",args,tx);
        return await Get(db,tx,owner,key);
    },ct);
    public Task<EventRecord> Activate(Guid owner,Guid id,string source,bool acceptDefaultAppearance,CancellationToken ct) => repo.Locked(owner,async(db,tx)=>
    {
        var item=await Get(db,tx,owner,id);
        if(item.ActivatedAt is not null)return item;
        EventPolicy.ValidateActivation(item,DateTime.UtcNow);
        if(source=="free"&&!acceptDefaultAppearance)throw new BillingRuleException(409,"Confirme o uso da aparência padrão Momentte para iniciar gratuitamente.");
        Guid? cycleId=null;
        if(source=="subscription")
        {
            var cycle=await repo.Current(db,tx,owner);
            if(cycle is null||cycle.ActivationsUsed>=8)throw new BillingRuleException(409,"Sem ativações disponíveis neste ciclo.");
            cycleId=cycle.Id;await db.ExecuteAsync("UPDATE billing_cycles SET activations_used=activations_used+1 WHERE id=@cycleId",new{cycleId},tx);
        }
        else if(source=="purchase")
        {
            var credit=await db.QuerySingleOrDefaultAsync<Guid?>("SELECT id FROM billing_orders WHERE owner_id=@owner AND kind='event' AND status='paid' AND event_id IS NULL ORDER BY created_at LIMIT 1 FOR UPDATE",new{owner},tx);
            if(credit is null)throw new BillingRuleException(409,"Não há crédito avulso aprovado na sua conta.");
            await db.ExecuteAsync("UPDATE billing_orders SET status='consumed',event_id=@id WHERE id=@credit",new{id,credit},tx);
        }
        else if(source!="free")throw new BillingRuleException(400,"Origem de ativação inválida.");
        var paid=source!="free";
        // A configuração permanece salva; propriedades Effective* ignoram a aparência paga no gratuito.
        await db.ExecuteAsync("UPDATE events SET activated_at=clock_timestamp(),paid=@paid,activation_source=@source,cycle_id=@cycleId,storage_limit_bytes=@bytes,download_hours=@hours WHERE id=@id",new{id,paid,source,cycleId,bytes=paid?BillingPolicy.PaidEventBytes:BillingPolicy.FreeEventBytes,hours=paid?72:24},tx);
        return await Get(db,tx,owner,id);
    },ct);
    public async Task End(Guid owner,Guid id,CancellationToken ct)=>await repo.Locked(owner,async(db,tx)=>
    {var item=await Get(db,tx,owner,id);if(item.ActivatedAt is null)throw new BillingRuleException(409,"O evento ainda não foi iniciado.");if(item.State=="active")await db.ExecuteAsync("UPDATE events SET ended_at=LEAST(clock_timestamp(),ends_at) WHERE id=@id",new{id},tx);return true;},ct);
    public async Task Delete(Guid owner,Guid id,CancellationToken ct)=>await repo.Locked(owner,async(db,tx)=>
    {var item=await Get(db,tx,owner,id);if(item.ActivatedAt is not null)throw new BillingRuleException(409,"Somente eventos em organização podem ser excluídos.");await db.ExecuteAsync("UPDATE events SET deleted_at=CURRENT_TIMESTAMP WHERE id=@id",new{id},tx);return true;},ct);
    public async Task ClearCustomization(Guid owner,Guid id,CancellationToken ct)=>await repo.Locked(owner,async(db,tx)=>
    {
        var e=await Get(db,tx,owner,id);if(e.ActivatedAt is not null)throw new BillingRuleException(409,"A configuração está travada.");
        if(e.CustomLogoPath is not null)await db.ExecuteAsync("INSERT INTO media_delete_queue(relative_path) VALUES(@path) ON CONFLICT DO NOTHING",new{path=e.CustomLogoPath},tx);
        await db.ExecuteAsync("UPDATE events SET custom_logo_path=NULL,custom_logo_type=NULL,theme_color=NULL,text_color='#fffaf1',font_family='sans',text_scale='medium',storage_used_bytes=storage_used_bytes-custom_logo_bytes,custom_logo_bytes=0 WHERE id=@id",new{id},tx);return true;
    },ct);
    public async Task RemoveLogo(Guid owner,Guid id,CancellationToken ct)=>await repo.Locked(owner,async(db,tx)=>
    {
        var e=await Get(db,tx,owner,id);if(e.ActivatedAt is not null)throw new BillingRuleException(409,"A configuração está travada.");
        if(e.CustomLogoPath is not null)await db.ExecuteAsync("INSERT INTO media_delete_queue(relative_path) VALUES(@path) ON CONFLICT DO NOTHING",new{path=e.CustomLogoPath},tx);
        await db.ExecuteAsync("UPDATE events SET custom_logo_path=NULL,custom_logo_type=NULL,storage_used_bytes=storage_used_bytes-custom_logo_bytes,custom_logo_bytes=0 WHERE id=@id",new{id},tx);return true;
    },ct);
    public async Task<object> Upload(Guid owner,Guid id,Stream input,bool logo,CancellationToken ct)
    {
        if(!logo){using var db=factory.CreateConnection();var e=await Get(db,null,owner,id);return await Submit(owner,e.PublicCode,null,input,false,ct);}
        string? uploaded=null;
        try
        {
            var result=await repo.Locked<object>(owner,async(db,tx)=>
            {
                var e=await Get(db,tx,owner,id);if(e.ActivatedAt is not null)throw new BillingRuleException(409,"A logo só pode ser alterada em organização.");
                var saved=await store.Put(input,id,5_000_000,ct);uploaded=saved.Path;
                if(e.CustomLogoPath is not null)await db.ExecuteAsync("INSERT INTO media_delete_queue(relative_path) VALUES(@path) ON CONFLICT DO NOTHING",new{path=e.CustomLogoPath},tx);
                await db.ExecuteAsync("UPDATE events SET custom_logo_path=@path,custom_logo_type=@type,custom_logo_bytes=@bytes,storage_used_bytes=storage_used_bytes-custom_logo_bytes+@bytes WHERE id=@id",new{id,path=saved.Path,type=saved.ContentType,bytes=saved.Bytes},tx);
                return new{bytes=saved.Bytes};
            },ct);uploaded=null;return result;
        }catch{if(uploaded is not null)await store.Delete(uploaded);throw;}
    }
    public async Task<PublicEventInfo> PublicInfo(string code,CancellationToken ct){using var db=factory.CreateConnection();return Public(await GetCode(db,null,code));}
    public async Task Join(Guid user,string code,CancellationToken ct)
    {
        using var read=factory.CreateConnection();var info=await GetCode(read,null,code);
        await repo.Locked(info.OwnerId,async(db,tx)=>{var e=await GetCode(db,tx,code);if(e.State=="finalized")throw new BillingRuleException(410,"Este evento já foi finalizado.");await db.ExecuteAsync("INSERT INTO event_participants(event_id,user_id) VALUES(@id,@user) ON CONFLICT DO NOTHING",new{id=e.Id,user},tx);return true;},ct);
    }
    public async Task<IEnumerable<PublicEventInfo>> Participations(Guid user,CancellationToken ct)
    {
        using var db=factory.CreateConnection();var events=await db.QueryAsync<EventRecord>(Select+"WHERE owner_id<>@user AND deleted_at IS NULL AND EXISTS(SELECT 1 FROM event_participants p WHERE p.event_id=events.id AND p.user_id=@user) ORDER BY created_at DESC",new{user});return events.Select(Public).ToArray();
    }
    public async Task<MediaRecord> Submit(Guid user,string code,string? text,Stream? image,bool showName,CancellationToken ct)
    {
        using var read=factory.CreateConnection();var info=await GetCode(read,null,code);string? uploaded=null;
        try
        {
            var result=await repo.Locked(info.OwnerId,async(db,tx)=>
            {
                var e=await GetCode(db,tx,code);await Access(db,tx,user,e);if(e.State!="active")throw new BillingRuleException(409,"O evento não está recebendo envios.");
                text=string.IsNullOrWhiteSpace(text)?null:text.Trim();EventPolicy.ValidateContent(e.MediaMode,image is not null,text);
                var media=new MediaRecord{Id=Guid.NewGuid(),EventId=e.Id,Text=text,Status=e.ModerationEnabled?"pending":"approved",CreatedAt=DateTime.UtcNow};
                if(image is not null){var saved=await store.Put(image,e.Id,Math.Min(20_000_000,e.StorageLimitBytes-e.StorageUsedBytes),ct);uploaded=saved.Path;media.RelativePath=saved.Path;media.ContentType=saved.ContentType;media.Bytes=saved.Bytes;}
                // O evento pode terminar enquanto o arquivo é lido; não aceita publicação fora do prazo.
                if(e.State!="active")throw new BillingRuleException(409,"O evento terminou durante o envio.");
                await db.ExecuteAsync("INSERT INTO event_media(id,event_id,relative_path,content_type,bytes,text_content,author_id,moderation_status,created_at,display_author_name) VALUES(@Id,@EventId,@RelativePath,@ContentType,@Bytes,@Text,@user,@Status,@CreatedAt,@showName); UPDATE events SET storage_used_bytes=storage_used_bytes+@Bytes WHERE id=@EventId",new{media.Id,media.EventId,media.RelativePath,media.ContentType,media.Bytes,media.Text,user,media.Status,media.CreatedAt,showName},tx);
                return media;
            },ct);uploaded=null;return result;
        }catch{if(uploaded is not null)await store.Delete(uploaded);throw;}
    }
    public async Task<IEnumerable<MediaRecord>> Submissions(Guid user,Guid? ownerEvent,string? code,bool feed,CancellationToken ct)
    {
        using var db=factory.CreateConnection();var e=ownerEvent is not null?await Get(db,null,user,ownerEvent.Value):await GetCode(db,null,code!);
        if(ownerEvent is null)await Access(db,null,user,e);
        if(e.State=="finalized")throw new BillingRuleException(410,"As mídias expiraram.");
        if(feed&&e.State!="active")throw new BillingRuleException(409,"O telão está disponível durante o evento ativo.");
        var filter=ownerEvent is null?" AND m.author_id=@user":feed?" AND m.moderation_status='approved'":"";
        return await db.QueryAsync<MediaRecord>(MediaSelect+"WHERE m.event_id=@id AND m.deleted_at IS NULL"+filter+(ownerEvent is not null&&!feed?" ORDER BY CASE WHEN m.moderation_status='pending' THEN 0 ELSE 1 END,m.created_at DESC LIMIT 200":" ORDER BY m.created_at DESC LIMIT 200"),new{id=e.Id,user,names=ownerEvent is not null&&!feed});
    }
    public async Task Moderate(Guid owner,Guid id,Guid mediaId,string status,CancellationToken ct)=>await repo.Locked(owner,async(db,tx)=>
    {
        if(status is not ("approved" or "rejected" or "pending"))throw new BillingRuleException(400,"Situação inválida.");
        var e=await Get(db,tx,owner,id);if(e.State is not ("active" or "awaiting_download"))throw new BillingRuleException(409,"O evento não aceita mais moderação.");
        if(await db.ExecuteAsync("UPDATE event_media SET moderation_status=@status WHERE id=@mediaId AND event_id=@id AND deleted_at IS NULL",new{status,mediaId,id},tx)==0)throw new BillingRuleException(404,"Envio não encontrado.");return true;
    },ct);
    public async Task DeleteSubmission(Guid owner,Guid id,Guid mediaId,CancellationToken ct)=>await repo.Locked(owner,async(db,tx)=>
    {
        var e=await Get(db,tx,owner,id);if(e.State=="finalized")throw new BillingRuleException(410,"As mídias expiraram.");
        var media=await db.QuerySingleOrDefaultAsync<MediaRecord>("SELECT bytes AS Bytes,relative_path AS RelativePath FROM event_media WHERE id=@mediaId AND event_id=@id AND deleted_at IS NULL",new{mediaId,id},tx)??throw new BillingRuleException(404,"Envio não encontrado.");
        if(media.RelativePath is not null)await db.ExecuteAsync("INSERT INTO media_delete_queue(relative_path) VALUES(@path) ON CONFLICT DO NOTHING",new{path=media.RelativePath},tx);
        await db.ExecuteAsync("UPDATE event_media SET deleted_at=CURRENT_TIMESTAMP,text_content=NULL WHERE id=@mediaId; UPDATE events SET storage_used_bytes=GREATEST(0,storage_used_bytes-@bytes) WHERE id=@id",new{mediaId,id,bytes=media.Bytes},tx);return true;
    },ct);
    public async Task<IEnumerable<MediaRecord>> Media(Guid owner,Guid id,CancellationToken ct)
    {
        using var db=factory.CreateConnection();var e=await Get(db,null,owner,id);if(!e.CanDownload)throw new BillingRuleException(410,"Downloads são liberados após encerrar e somente dentro do prazo.");
        return await db.QueryAsync<MediaRecord>(MediaSelect+"WHERE m.event_id=@id AND m.deleted_at IS NULL AND m.moderation_status='approved' ORDER BY m.created_at",new{id,names=false});
    }
    public async Task<(Stream Stream,string Type)> Preview(Guid user,Guid? ownerEvent,string? code,Guid mediaId,CancellationToken ct)
    {
        using var db=factory.CreateConnection();var e=ownerEvent is not null?await Get(db,null,user,ownerEvent.Value):await GetCode(db,null,code!);if(ownerEvent is null)await Access(db,null,user,e);
        if(e.State=="finalized")throw new BillingRuleException(410,"As mídias expiraram.");
        var media=await db.QuerySingleOrDefaultAsync<MediaRecord>("SELECT relative_path AS RelativePath,content_type AS ContentType FROM event_media WHERE id=@mediaId AND event_id=@id AND deleted_at IS NULL"+(ownerEvent is null?" AND author_id=@user":""),new{mediaId,id=e.Id,user})??throw new BillingRuleException(404,"Imagem não encontrada.");
        if(media.RelativePath is null)throw new BillingRuleException(404,"Este envio contém somente texto.");return(store.Read(media.RelativePath),media.ContentType);
    }
    public async Task<(Stream Stream,string Type)> Download(Guid owner,Guid id,Guid? mediaId,CancellationToken ct)
    {
        using var db=factory.CreateConnection();var e=await Get(db,null,owner,id);
        if(mediaId is null){if(e.CustomLogoPath is null||e.State=="finalized")throw new BillingRuleException(404,"Logo não encontrada.");return(store.Read(e.CustomLogoPath),e.CustomLogoType??"application/octet-stream");}
        if(!e.CanDownload)throw new BillingRuleException(410,"Downloads são liberados após encerrar e somente dentro do prazo.");
        var media=await db.QuerySingleOrDefaultAsync<MediaRecord>("SELECT relative_path AS RelativePath,content_type AS ContentType FROM event_media WHERE id=@mediaId AND event_id=@id AND deleted_at IS NULL AND moderation_status='approved'",new{mediaId,id})??throw new BillingRuleException(404,"Imagem não encontrada.");if(media.RelativePath is null)throw new BillingRuleException(404,"Este envio contém somente texto.");return(store.Read(media.RelativePath),media.ContentType);
    }
    public async Task<(Stream Stream,string Type)> PublicLogo(Guid user,string code,CancellationToken ct)
    {using var db=factory.CreateConnection();var e=await GetCode(db,null,code);await Access(db,null,user,e);if(!e.Paid||e.CustomLogoPath is null||e.State=="finalized")throw new BillingRuleException(404,"Logo não disponível.");return(store.Read(e.CustomLogoPath),e.CustomLogoType??"application/octet-stream");}
    public async Task<string> ExportTexts(Guid owner,Guid id,CancellationToken ct)
    {
        var media=await Media(owner,id,ct);static string Cell(string? value){var text=value??"";if(text.TrimStart().StartsWith('=')||text.TrimStart().StartsWith('+')||text.TrimStart().StartsWith('-')||text.TrimStart().StartsWith('@'))text="'"+text;return "\""+text.Replace("\"","\"\"")+"\"";}
        return "Id;Tipo;Texto ou legenda;Nome exibido;Data UTC\r\n"+string.Join("\r\n",media.Select(m=>$"{m.Id};{(m.HasImage?"imagem":"texto")};{Cell(m.Text)};{Cell(m.AuthorName)};{m.CreatedAt:O}"));
    }
    public async Task Purge(CancellationToken ct)
    {
        using var db=factory.CreateConnection();
        foreach(var path in await db.QueryAsync<string>("SELECT relative_path FROM media_delete_queue LIMIT 100")){await store.Delete(path);await db.ExecuteAsync("DELETE FROM media_delete_queue WHERE relative_path=@path",new{path});}
        var items=await db.QueryAsync<MediaRecord>("SELECT m.id AS Id,m.relative_path AS RelativePath FROM event_media m JOIN events e ON e.id=m.event_id WHERE m.deleted_at IS NULL AND (e.deleted_at IS NOT NULL OR e.activated_at IS NOT NULL AND COALESCE(e.ended_at,e.ends_at)+(e.download_hours*INTERVAL '1 hour')<=CURRENT_TIMESTAMP)");
        foreach(var item in items){ct.ThrowIfCancellationRequested();if(item.RelativePath is not null)await store.Delete(item.RelativePath);await db.ExecuteAsync("WITH removed AS(UPDATE event_media SET deleted_at=CURRENT_TIMESTAMP,text_content=NULL,author_id=NULL WHERE id=@id AND deleted_at IS NULL RETURNING bytes,event_id) UPDATE events e SET storage_used_bytes=GREATEST(0,e.storage_used_bytes-r.bytes) FROM removed r WHERE e.id=r.event_id",new{id=item.Id});}
        var logos=await db.QueryAsync<EventRecord>(Select+"WHERE custom_logo_path IS NOT NULL AND (deleted_at IS NOT NULL OR activated_at IS NOT NULL AND COALESCE(ended_at,ends_at)+(download_hours*INTERVAL '1 hour')<=CURRENT_TIMESTAMP)");
        foreach(var e in logos){await store.Delete(e.CustomLogoPath!);await db.ExecuteAsync("UPDATE events SET custom_logo_path=NULL,custom_logo_type=NULL,storage_used_bytes=GREATEST(0,storage_used_bytes-custom_logo_bytes),custom_logo_bytes=0 WHERE id=@id",new{id=e.Id});}
    }
}
