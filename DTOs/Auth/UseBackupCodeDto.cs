namespace MyPasswordVault.API.DTOs.Auth;

public class UseBackupCodeDto
{
    public string PendingToken { get; set; } = null!;
    public string Code { get; set; } = null!;
}
