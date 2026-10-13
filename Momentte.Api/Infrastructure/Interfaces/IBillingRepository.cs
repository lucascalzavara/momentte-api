using System.Data;
using Momentte.Api.Domain.Billing;
namespace Momentte.Api.Infrastructure.Interfaces;
public interface IBillingRepository
{
    Task<T> Locked<T>(Guid owner, Func<IDbConnection, IDbTransaction, Task<T>> action, CancellationToken ct = default);
    Task<BillingCycle?> Current(IDbConnection db, IDbTransaction tx, Guid owner);
    Task<BillingContract?> Contract(IDbConnection db, IDbTransaction tx, Guid owner);
}
