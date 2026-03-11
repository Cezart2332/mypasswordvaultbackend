namespace MyPasswordVault.API.Models;

public class KnownDevice
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 hash of "IP|UserAgent" — used for quick lookup without storing the raw UA.</summary>
    public string DeviceHash { get; set; } = string.Empty;

    /// <summary>Client IP address — shown in the alert email.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>Truncated User-Agent string — shown in the alert email.</summary>
    public string UserAgentDisplay { get; set; } = string.Empty;

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
