using EAIOS.Api.Domain.Notification;

namespace EAIOS.Api.Application.Notification;

public sealed record NotificationDto(
    Guid Id,
    NotificationChannel Channel,
    string Type,
    string Title,
    string? Body,
    string? ActionUrl,
    string? ActionLabel,
    NotificationPriority Priority,
    NotificationStatus Status,
    bool IsRead,
    DateTime? SentAt,
    DateTime? ReadAt,
    DateTime CreatedAt);

public sealed record NotificationPreferencesDto(
    bool InAppEnabled,
    bool EmailEnabled,
    bool SmsEnabled,
    bool PushEnabled,
    string DigestFrequency,
    IReadOnlyList<ChannelPreferenceDto> ChannelOverrides);

public sealed record ChannelPreferenceDto(string EventType, NotificationChannel Channel, bool Enabled);

public sealed record UpdatePreferencesRequest(
    bool? InAppEnabled,
    bool? EmailEnabled,
    bool? SmsEnabled,
    bool? PushEnabled,
    string? DigestFrequency,
    IReadOnlyList<ChannelPreferenceDto>? ChannelOverrides);
