using System.Data;
using Dapper;
using Momentte.Api.Domain.Billing;
using Momentte.Api.Domain.Events;
using Momentte.Api.Infrastructure.Interfaces;
using Momentte.Api.Application.Interfaces;
namespace Momentte.Api.Application.Implementations;
public sealed class EventService(IBillingRepository repo, IDbConnectionFactory factory, IMediaStore store) : IEventService
{
    private const string Select = "SELECT id AS Id,owner_id AS OwnerId,name AS Name,ends_at AS EndsAt,activated_at AS ActivatedAt,ended_at AS EndedAt,paid AS Paid,activation_source AS ActivationSource,storage_limit_bytes AS StorageLimitBytes,storage_used_bytes AS StorageUsedBytes,download_hours AS DownloadHours,custom_logo_path AS CustomLogoPath,custom_logo_type AS CustomLogoType,custom_logo_bytes AS CustomLogoBytes,theme_color AS ThemeColor FROM events ";
    private async Task<EventRecord> Get(IDbConnection db, IDbTransaction? tx, Guid owner, Guid id)
        => await db.QuerySingleOrDefaultAsync<EventRecord>(Select + "WHERE id=@id AND owner_id=@owner AND deleted_at IS NULL", new { id, owner }, tx) ?? throw new BillingRuleException(404, "Evento não encontrado.");
    public async Task<IEnumerable<EventRecord>> List(Guid owner, CancellationToken ct)
    { using var db = factory.CreateConnection(); return await db.QueryAsync<EventRecord>(Select + "WHERE owner_id=@owner AND deleted_at IS NULL ORDER BY created_at DESC", new { owner }); }
    private async Task<bool> CanCustomize(IDbConnection db, IDbTransaction tx, Guid owner, Guid id)
    {
        var cycle = await repo.Current(db, tx, owner);
        return cycle is { ActivationsUsed: < 8 } || await db.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_orders WHERE owner_id=@owner AND status='paid' AND kind='event' AND (event_id=@id OR event_id IS NULL)", new { owner, id }, tx) > 0;
    }
    public Task<EventRecord> Save(Guid owner, Guid? id, EventRequest request, CancellationToken ct) => repo.Locked(owner, async (db, tx) =>
    {
        if (request.EndsAt.UtcDateTime <= DateTime.UtcNow || string.IsNullOrWhiteSpace(request.Name)) throw new BillingRuleException(400, "Informe um nome e término futuro.");
        var key = id ?? Guid.NewGuid();
        if (id is not null && (await Get(db, tx, owner, key)).ActivatedAt is not null) throw new BillingRuleException(409, "Nome, término e customização ficam travados após iniciar.");
        if (request.ThemeColor is not null && !await CanCustomize(db, tx, owner, key)) throw new BillingRuleException(403, "Customização completa exige um evento pago ou uma ativação disponível.");
        if (id is null)
            await db.ExecuteAsync("INSERT INTO events(id,owner_id,name,ends_at,theme_color) VALUES(@key,@owner,@name,@end,@theme)", new { key, owner, name = request.Name.Trim(), end = request.EndsAt.UtcDateTime, theme = request.ThemeColor }, tx);
        else await db.ExecuteAsync("UPDATE events SET name=@name,ends_at=@end,theme_color=@theme WHERE id=@key", new { key, name = request.Name.Trim(), end = request.EndsAt.UtcDateTime, theme = request.ThemeColor }, tx);
        return await Get(db, tx, owner, key);
    }, ct);
    public Task<EventRecord> Activate(Guid owner, Guid id, string source, CancellationToken ct) => repo.Locked(owner, async (db, tx) =>
    {
        var item = await Get(db, tx, owner, id);
        if (item.ActivatedAt is not null) return item; // Repetir não consome duas vezes.
        if (item.EndsAt <= DateTime.UtcNow) throw new BillingRuleException(409, "Atualize o término antes de iniciar o evento.");
        Guid? cycleId = null;
        if (source == "subscription")
        {
            var cycle = await repo.Current(db, tx, owner);
            if (cycle is null || cycle.ActivationsUsed >= 8) throw new BillingRuleException(409, "Sem ativações disponíveis neste ciclo. Compre um evento excedente.");
            cycleId = cycle.Id;
            await db.ExecuteAsync("UPDATE billing_cycles SET activations_used=activations_used+1 WHERE id=@cycleId", new { cycleId }, tx);
        }
        else if (source == "purchase")
        {
            var order = await db.QuerySingleOrDefaultAsync<Guid?>("SELECT id FROM billing_orders WHERE owner_id=@owner AND kind='event' AND status='paid' AND (event_id=@id OR event_id IS NULL) ORDER BY created_at LIMIT 1 FOR UPDATE", new { owner, id }, tx);
            if (order is null) throw new BillingRuleException(409, "Não há compra aprovada para este evento.");
            await db.ExecuteAsync("UPDATE billing_orders SET status='consumed',event_id=@id WHERE id=@order", new { id, order }, tx);
        }
        else if (source != "free") throw new BillingRuleException(400, "Origem de ativação inválida.");
        var paid = source != "free";
        if (!paid && (item.CustomLogoPath is not null || item.ThemeColor is not null)) throw new BillingRuleException(409, "Remova a customização ou inicie com recursos pagos.");
        if (source != "purchase") await db.ExecuteAsync("UPDATE billing_orders SET event_id=NULL WHERE event_id=@id AND status='paid' AND kind='event'", new { id }, tx);
        await db.ExecuteAsync("UPDATE events SET activated_at=CURRENT_TIMESTAMP,paid=@paid,activation_source=@source,cycle_id=@cycleId,storage_limit_bytes=@bytes,download_hours=@hours WHERE id=@id",
            new { id, paid, source, cycleId, bytes = paid ? BillingPolicy.PaidEventBytes : BillingPolicy.FreeEventBytes, hours = paid ? 72 : 24 }, tx);
        return await Get(db, tx, owner, id);
    }, ct);
    public async Task End(Guid owner, Guid id, CancellationToken ct) => await repo.Locked(owner, async (db, tx) =>
    {
        var item = await Get(db, tx, owner, id);
        if (item.State != "active") throw new BillingRuleException(409, "Somente eventos ativos podem ser encerrados.");
        await db.ExecuteAsync("UPDATE events SET ended_at=CURRENT_TIMESTAMP WHERE id=@id", new { id }, tx);
        return true;
    }, ct);
    public async Task Delete(Guid owner, Guid id, CancellationToken ct) => await repo.Locked(owner, async (db, tx) =>
    {
        var item = await Get(db, tx, owner, id);
        if (item.ActivatedAt is not null) throw new BillingRuleException(409, "Somente eventos em organização podem ser excluídos.");
        if (await db.ExecuteScalarAsync<int>("SELECT count(*) FROM billing_orders WHERE event_id=@id AND status='pending'", new { id }, tx) > 0) throw new BillingRuleException(409, "Há um pagamento pendente para este evento.");
        await db.ExecuteAsync("UPDATE events SET deleted_at=CURRENT_TIMESTAMP WHERE id=@id; UPDATE billing_orders SET event_id=NULL WHERE event_id=@id AND status='paid'", new { id }, tx);
        // Uma compra ainda não consumida permanece como crédito para outro evento.
        return true;
    }, ct);
    public async Task ClearCustomization(Guid owner, Guid id, CancellationToken ct) => await repo.Locked(owner, async (db, tx) =>
    {
        var item = await Get(db, tx, owner, id);
        if (item.ActivatedAt is not null) throw new BillingRuleException(409, "A customização está travada após iniciar.");
        if (item.CustomLogoPath is not null) await db.ExecuteAsync("INSERT INTO media_delete_queue(relative_path) VALUES(@path) ON CONFLICT DO NOTHING", new { path = item.CustomLogoPath }, tx);
        await db.ExecuteAsync("UPDATE events SET custom_logo_path=NULL,custom_logo_type=NULL,theme_color=NULL,storage_used_bytes=storage_used_bytes-custom_logo_bytes,custom_logo_bytes=0 WHERE id=@id", new { id }, tx);
        return true;
    }, ct);
    public async Task<object> Upload(Guid owner, Guid id, Stream input, bool logo, CancellationToken ct)
    {
        string? uploaded = null;
        try
        {
            var result = await repo.Locked<object>(owner, async (db, tx) =>
            {
                var item = await Get(db, tx, owner, id);
                if (logo ? item.ActivatedAt is not null || !await CanCustomize(db, tx, owner, id) : item.State != "active") throw new BillingRuleException(403, logo ? "Logo própria é configurada antes de iniciar um evento pago." : "O evento não está recebendo mídias.");
                var remaining = logo ? 5_000_000 : item.StorageLimitBytes - item.StorageUsedBytes;
                var saved = await store.Put(input, id, Math.Min(remaining, 50_000_000), ct);
                uploaded = saved.Path;
                if (logo)
                {
                    if (item.CustomLogoPath is not null) await db.ExecuteAsync("INSERT INTO media_delete_queue(relative_path) VALUES(@path) ON CONFLICT DO NOTHING", new { path = item.CustomLogoPath }, tx);
                    await db.ExecuteAsync("UPDATE events SET custom_logo_path=@path,custom_logo_type=@type,custom_logo_bytes=@bytes,storage_used_bytes=storage_used_bytes-custom_logo_bytes+@bytes WHERE id=@id", new { id, path = saved.Path, type = saved.ContentType, bytes = saved.Bytes }, tx);
                }
                else
                {
                    await db.ExecuteAsync("INSERT INTO event_media(id,event_id,relative_path,content_type,bytes) VALUES(@key,@id,@path,@type,@bytes); UPDATE events SET storage_used_bytes=storage_used_bytes+@bytes WHERE id=@id", new { key = Guid.NewGuid(), id, path = saved.Path, type = saved.ContentType, bytes = saved.Bytes }, tx);
                }
                return new { bytes = saved.Bytes };
            }, ct);
            uploaded = null; // Commit concluído; não apaga um arquivo já referenciado.
            return result;
        }
        catch { if (uploaded is not null) await store.Delete(uploaded); throw; }
    }
    public async Task<IEnumerable<MediaRecord>> Media(Guid owner, Guid id, CancellationToken ct)
    {
        using var db = factory.CreateConnection(); var item = await Get(db, null, owner, id);
        if (!item.CanDownload) throw new BillingRuleException(410, "As mídias não estão disponíveis para download.");
        return await db.QueryAsync<MediaRecord>("SELECT id AS Id,event_id AS EventId,content_type AS ContentType,bytes AS Bytes FROM event_media WHERE event_id=@id AND deleted_at IS NULL", new { id });
    }
    public async Task<(Stream Stream, string Type)> Download(Guid owner, Guid id, Guid? mediaId, CancellationToken ct)
    {
        using var db = factory.CreateConnection(); var item = await Get(db, null, owner, id);
        if (!item.CanDownload) throw new BillingRuleException(410, "O prazo de download terminou ou o evento ainda não foi iniciado.");
        if (mediaId is null)
        {
            if (!item.Paid || item.CustomLogoPath is null) throw new BillingRuleException(404, "Logo própria não configurada.");
            return (store.Read(item.CustomLogoPath), item.CustomLogoType ?? "application/octet-stream");
        }
        var media = await db.QuerySingleOrDefaultAsync<MediaRecord>("SELECT relative_path AS RelativePath,content_type AS ContentType FROM event_media WHERE id=@mediaId AND event_id=@id AND deleted_at IS NULL", new { mediaId, id }) ?? throw new BillingRuleException(404, "Mídia não encontrada.");
        return (store.Read(media.RelativePath), media.ContentType);
    }
    public async Task Purge(CancellationToken ct)
    {
        using var db = factory.CreateConnection();
        var queued = await db.QueryAsync<string>("SELECT relative_path FROM media_delete_queue LIMIT 100");
        foreach (var path in queued) { await store.Delete(path); await db.ExecuteAsync("DELETE FROM media_delete_queue WHERE relative_path=@path", new { path }); }
        var media = await db.QueryAsync<MediaRecord>("SELECT m.id AS Id,m.event_id AS EventId,m.bytes AS Bytes,m.relative_path AS RelativePath FROM event_media m JOIN events e ON e.id=m.event_id WHERE m.deleted_at IS NULL AND (e.deleted_at IS NOT NULL OR COALESCE(e.ended_at,e.ends_at)+(e.download_hours*INTERVAL '1 hour')<=CURRENT_TIMESTAMP AND e.activated_at IS NOT NULL)");
        foreach (var item in media) { ct.ThrowIfCancellationRequested(); await store.Delete(item.RelativePath); await db.ExecuteAsync("WITH removed AS (UPDATE event_media SET deleted_at=CURRENT_TIMESTAMP WHERE id=@id AND deleted_at IS NULL RETURNING bytes,event_id) UPDATE events e SET storage_used_bytes=GREATEST(0,e.storage_used_bytes-r.bytes) FROM removed r WHERE e.id=r.event_id", new { id = item.Id }); }
        var logos = await db.QueryAsync<EventRecord>(Select + "WHERE custom_logo_path IS NOT NULL AND (deleted_at IS NOT NULL OR activated_at IS NOT NULL AND COALESCE(ended_at,ends_at)+(download_hours*INTERVAL '1 hour')<=CURRENT_TIMESTAMP)");
        foreach (var item in logos) { await store.Delete(item.CustomLogoPath!); await db.ExecuteAsync("UPDATE events SET custom_logo_path=NULL,custom_logo_type=NULL,storage_used_bytes=GREATEST(0,storage_used_bytes-custom_logo_bytes),custom_logo_bytes=0 WHERE id=@id", new { id = item.Id }); }
    }
}
