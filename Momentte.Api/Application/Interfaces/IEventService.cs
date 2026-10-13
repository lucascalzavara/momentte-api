using Momentte.Api.Domain.Events;
namespace Momentte.Api.Application.Interfaces;
public interface IEventService
{
    Task<IEnumerable<EventRecord>> List(Guid owner, CancellationToken ct);
    Task<EventRecord> Save(Guid owner, Guid? id, EventRequest request, CancellationToken ct);
    Task<EventRecord> Activate(Guid owner, Guid id, string source, CancellationToken ct);
    Task End(Guid owner, Guid id, CancellationToken ct);
    Task Delete(Guid owner, Guid id, CancellationToken ct);
    Task ClearCustomization(Guid owner, Guid id, CancellationToken ct);
    Task<object> Upload(Guid owner, Guid id, Stream input, bool logo, CancellationToken ct);
    Task<IEnumerable<MediaRecord>> Media(Guid owner, Guid id, CancellationToken ct);
    Task<(Stream Stream, string Type)> Download(Guid owner, Guid id, Guid? mediaId, CancellationToken ct);
    Task Purge(CancellationToken ct);
}
