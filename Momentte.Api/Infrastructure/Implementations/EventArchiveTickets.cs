using System.Security.Cryptography;
using System.Text;
using Momentte.Api.Domain.Billing;

namespace Momentte.Api.Infrastructure.Implementations;

// A short-lived, single-use capability permits a native browser download without exposing the JWT.
public sealed class EventArchiveTickets(TimeProvider clock)
{
    private sealed record Ticket(Guid EventId, byte[] Hash, DateTimeOffset ExpiresAt);
    private readonly Dictionary<Guid, Ticket> tickets = [];
    private readonly object gate = new();

    public string Issue(Guid owner, Guid eventId)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            foreach (var key in tickets.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
                tickets.Remove(key);
            if (tickets.Count >= 10000 && !tickets.ContainsKey(owner))
                throw new BillingRuleException(503, "Downloads ocupados. Tente novamente em instantes.");
            var token = owner.ToString("N") + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            tickets[owner] = new(eventId, SHA256.HashData(Encoding.UTF8.GetBytes(token)), now.AddMinutes(2));
            return token;
        }
    }

    public Guid Consume(Guid eventId, string token)
    {
        if (token.Length != 97 || token[32] != '.' || !Guid.TryParseExact(token[..32], "N", out var owner))
            throw Invalid();
        lock (gate)
        {
            if (!tickets.TryGetValue(owner, out var ticket) || ticket.EventId != eventId ||
                ticket.ExpiresAt <= clock.GetUtcNow() ||
                !CryptographicOperations.FixedTimeEquals(ticket.Hash, SHA256.HashData(Encoding.UTF8.GetBytes(token))))
                throw Invalid();
            tickets.Remove(owner);
            return owner;
        }
    }

    private static BillingRuleException Invalid() => new(401, "Link de download inválido ou expirado. Solicite o ZIP novamente.");
}
