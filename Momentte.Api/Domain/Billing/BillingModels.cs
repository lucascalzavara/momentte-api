using System.ComponentModel.DataAnnotations;
namespace Momentte.Api.Domain.Billing;
public sealed class BillingAccount { public Guid OwnerId { get; set; } }
public sealed class BillingCycle
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid ContractId { get; set; }
    public string PlanCode { get; set; } = "free";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public int ActivationsUsed { get; set; }
}
public sealed class BillingContract
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string PlanCode { get; set; } = "free";
    public string? PendingPlan { get; set; }
    public string? ProviderId { get; set; }
    public string? CheckoutUrl { get; set; }
    public bool Cancelled { get; set; }
}
public sealed class BillingOrder
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid RequestKey { get; set; }
    public string Kind { get; set; } = "";
    public string PlanCode { get; set; } = "";
    public string? PreviousPlan { get; set; }
    public Guid? EventId { get; set; }
    public Guid? CycleId { get; set; }
    public int AmountCents { get; set; }
    public string Status { get; set; } = "pending";
    public string? ProviderId { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime? ValidUntil { get; set; }
}
public sealed class CheckoutRequest
{
    [Required] public string Kind { get; set; } = "subscription";
    [Required] public string PlanCode { get; set; } = "professional";
    public Guid? EventId { get; set; }
    public Guid RequestKey { get; set; }
}
public sealed class PlanChangeRequest { [Required] public string PlanCode { get; set; } = "free"; }
public sealed record BillingOverview(PlanDefinition Plan, BillingCycle? Cycle, BillingContract? Contract, IReadOnlyList<PlanDefinition> Plans, IEnumerable<BillingOrder> Orders, bool CheckoutAvailable)
{public int AvailableEventCredits {get;init;} }
