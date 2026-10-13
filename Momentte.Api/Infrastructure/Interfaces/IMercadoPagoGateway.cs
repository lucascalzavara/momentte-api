using System.Text.Json;
namespace Momentte.Api.Infrastructure.Interfaces;
public interface IMercadoPagoGateway
{
    bool Available { get; }
    Task<JsonElement> Send(HttpMethod method, string path, object? body = null, string? idempotencyKey = null, CancellationToken ct = default);
    Task<(string Id, string Url)> Checkout(Guid id, string title, int cents, string email, bool subscription, DateTime? expires, CancellationToken ct);
    bool VerifySignature(string dataId, string requestId, string signature);
}
