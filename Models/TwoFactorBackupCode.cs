namespace MyPasswordVault.API.Models;

public class TwoFactorBackupCode
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string CodeHash { get; set; } = null!;
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }
}
