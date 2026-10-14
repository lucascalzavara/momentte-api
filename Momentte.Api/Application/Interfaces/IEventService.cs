using Momentte.Api.Domain.Events;
namespace Momentte.Api.Application.Interfaces;
public interface IEventService
{
    Task<IEnumerable<EventRecord>> List(Guid owner, CancellationToken ct);
    Task<EventRecord> Save(Guid owner, Guid? id, EventRequest request, CancellationToken ct);
    Task<EventRecord> Activate(Guid owner, Guid id, string source, bool acceptDefaultAppearance, CancellationToken ct);
    Task End(Guid owner, Guid id, CancellationToken ct);
    Task Delete(Guid owner, Guid id, CancellationToken ct);
    Task ClearCustomization(Guid owner, Guid id, CancellationToken ct);
    Task RemoveLogo(Guid owner,Guid id,CancellationToken ct);
    Task<object> Upload(Guid owner, Guid id, Stream input, bool logo, CancellationToken ct);
    Task<IEnumerable<MediaRecord>> Media(Guid owner, Guid id, CancellationToken ct);
    Task<(Stream Stream, string Type)> Download(Guid owner, Guid id, Guid? mediaId, CancellationToken ct);
    Task Purge(CancellationToken ct);
    Task<PublicEventInfo> PublicInfo(string code, CancellationToken ct);
    Task Join(Guid user, string code, CancellationToken ct);
    Task<IEnumerable<PublicEventInfo>> Participations(Guid user, CancellationToken ct);
    Task<MediaRecord> Submit(Guid user, string code, string? text, Stream? image, bool showName, CancellationToken ct);
    Task<IEnumerable<MediaRecord>> Submissions(Guid user, Guid? ownerEvent, string? code, bool feed, CancellationToken ct);
    Task Moderate(Guid owner, Guid id, Guid mediaId, string status, CancellationToken ct);
    Task DeleteSubmission(Guid owner, Guid id, Guid mediaId, CancellationToken ct);
    Task<(Stream Stream,string Type)> Preview(Guid user, Guid? ownerEvent, string? code, Guid mediaId, CancellationToken ct);
    Task<(Stream Stream,string Type)> PublicLogo(Guid user,string code,CancellationToken ct);
    Task<string> ExportTexts(Guid owner,Guid id,CancellationToken ct);
}
