using System.ComponentModel.DataAnnotations;
using Momentte.Api.Domain.Billing;
namespace Momentte.Api.Domain.Events;
public sealed class EventRecord
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public DateTime EndsAt { get; set; }
    public DateTime StartsAt { get; set; }
    public string PublicCode { get; set; } = "";
    public string MediaMode { get; set; } = "images";
    public bool ModerationEnabled { get; set; }
    public string TextColor { get; set; } = "#fffaf1";
    public string FontFamily { get; set; } = "sans";
    public string TextScale { get; set; } = "medium";
    public string EffectiveBackground => Paid ? ThemeColor ?? "#2f241e" : "#2f241e";
    public string EffectiveTextColor => Paid ? TextColor : "#fffaf1";
    public string EffectiveFont => Paid ? FontFamily : "sans";
    public string EffectiveScale => Paid ? TextScale : "medium";
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
    public DateTime EffectiveEnd => EndedAt is not null&&EndedAt<EndsAt?EndedAt.Value:EndsAt;
    public DateTime? DownloadUntil => ActivatedAt is null ? null : EffectiveEnd.AddHours(DownloadHours);
    public string State => BillingPolicy.EventState(ActivatedAt, EffectiveEnd, DownloadHours, DateTime.UtcNow);
    public bool CanDownload => EventPolicy.DownloadAvailable(ActivatedAt,EffectiveEnd,DownloadHours,DateTime.UtcNow);
    public bool UsesMomentteBrand => !Paid || string.IsNullOrEmpty(CustomLogoPath);
}
public sealed class EventRequest
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    [RegularExpression("^#[0-9a-fA-F]{6}$")] public string? ThemeColor { get; set; }
    [Required, RegularExpression("^(images|texts|both)$")] public string MediaMode { get; set; } = "images";
    public bool ModerationEnabled { get; set; }
    [Required, RegularExpression("^#[0-9a-fA-F]{6}$")] public string TextColor { get; set; } = "#fffaf1";
    [Required, RegularExpression("^(sans|serif|rounded)$")] public string FontFamily { get; set; } = "sans";
    [Required, RegularExpression("^(small|medium|large)$")] public string TextScale { get; set; } = "medium";
}
public sealed class ActivateEventRequest { [Required] public string Source { get; set; } = "free"; public bool AcceptDefaultAppearance { get; set; } }
public sealed class MediaRecord
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? RelativePath { get; set; }
    public string ContentType { get; set; } = "";
    public long Bytes { get; set; }
    public bool HasImage => RelativePath is not null;
    public string? Text { get; set; }
    public string Status { get; set; } = "approved";
    public string? AuthorName { get; set; }
    public DateTime CreatedAt { get; set; }
}
public sealed record PublicEventInfo(string Name,string PublicCode,DateTime StartsAt,DateTime EndsAt,string State,string MediaMode,bool ModerationEnabled,string BackgroundColor,string TextColor,string FontFamily,string TextScale,bool HasCustomLogo);
public sealed class ModerateRequest { [Required,RegularExpression("^(approved|rejected|pending)$")] public string Status { get; set; } = "approved"; }
