namespace Momentte.Api.Infrastructure.Interfaces;
public interface IMediaStore
{
    Task<(string Path, long Bytes, string ContentType)> Put(Stream input, Guid eventId, long maximumBytes, CancellationToken ct);
    Stream Read(string relativePath);
    Task Delete(string relativePath);
}
