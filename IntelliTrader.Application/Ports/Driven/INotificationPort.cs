using IntelliTrader.Application.Common;

namespace IntelliTrader.Application.Ports.Driven;

/// <summary>
/// Port for sending notifications (e.g., Telegram, email).
/// This is a driven (secondary) port - the application defines it, infrastructure implements it.
/// </summary>
public interface INotificationPort
{
    /// <summary>
    /// Sends a notification message.
    /// </summary>
    Task<Result> SendAsync(
        Notification notification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends multiple notifications.
    /// </summary>
    Task<Result> SendManyAsync(
        IEnumerable<Notification> notifications,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connectivity to the notification service.
    /// </summary>
    Task<Result<bool>> TestConnectivityAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a notification to be sent.
/// </summary>
public sealed record Notification
{
    /// <summary>
    /// The title/subject of the notification.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// The message content.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// The type/category of notification.
    /// </summary>
    public NotificationType Type { get; init; } = NotificationType.Info;

    /// <summary>
    /// Priority level.
    /// </summary>
    public NotificationPriority Priority { get; init; } = NotificationPriority.Normal;

    /// <summary>
    /// Optional channel/recipient to send to.
    /// </summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Optional metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// When the notification was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Type of notification.
/// </summary>
public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error,
    Trade,
    Signal,
    System
}

/// <summary>
/// Priority level for notifications.
/// </summary>
public enum NotificationPriority
{
    Low,
    Normal,
    High,
    Critical
}
