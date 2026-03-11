namespace MyPasswordVault.API.Models;
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public string kdfSalt { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }

    public string? EmailVerificationToken { get; set; }
    public DateTime? EmailVerificationTokenExpiry { get; set; }
    public string? TwoFactorSecret { get; set; }
    public bool TwoFactorEnabled { get; set; } = false;
    public string? PendingTwoFactorToken { get; set; }
    public DateTime? PendingTwoFactorTokenExpiry { get; set; }
    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiry { get; set; }
    public bool isVerified { get; set; } = false;

    // Pending email change
    public string? PendingEmail { get; set; }
    public string? PendingEmailToken { get; set; }
    public DateTime? PendingEmailTokenExpiry { get; set; }
}