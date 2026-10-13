using Momentte.Api.Domain.Billing;
namespace Momentte.Api.Application.Interfaces;
public interface IBillingService
{
    Task<BillingOverview> Overview(Guid owner, CancellationToken ct);
    Task<object> Checkout(Guid owner, string email, CheckoutRequest request, CancellationToken ct);
    Task ChangePlan(Guid owner, string plan, CancellationToken ct);
    Task Notify(string topic, string id, CancellationToken ct);
    Task Reconcile(Guid owner, CancellationToken ct);
}
