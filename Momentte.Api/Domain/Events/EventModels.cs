using System.ComponentModel.DataAnnotations;
using Momentte.Api.Domain.Billing;
namespace Momentte.Api.Domain.Events;
public sealed class EventRecord
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public DateTime EndsAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool Paid { get; set; }
    public string? ActivationSource { get; set; }
    public long StorageLimitBytes { get; set; }
    public long StorageUsedBytes { get; set; }
    public int DownloadHours { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? CustomLogoPath { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? CustomLogoType { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public long CustomLogoBytes { get; set; }
    public bool HasCustomLogo => CustomLogoPath is not null;
    public string? ThemeColor { get; set; }
    public DateTime EffectiveEnd => EndedAt ?? EndsAt;
    public DateTime? DownloadUntil => ActivatedAt is null ? null : EffectiveEnd.AddHours(DownloadHours);
    public string State => BillingPolicy.EventState(ActivatedAt, EffectiveEnd, DownloadHours, DateTime.UtcNow);
    public bool CanDownload => ActivatedAt is not null && DateTime.UtcNow < DownloadUntil;
    public bool UsesMomentteBrand => !Paid || string.IsNullOrEmpty(CustomLogoPath);
}
public sealed class EventRequest
{
    [Required, StringLength(150)] public string Name { get; set; } = "";
    public DateTimeOffset EndsAt { get; set; }
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string? ThemeColor { get; set; }
}
public sealed class ActivateEventRequest { [Required] public string Source { get; set; } = "free"; }
public sealed class MediaRecord
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string RelativePath { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Bytes { get; set; }
}
