namespace MyPasswordVault.API.DTOs.User;

public class ChangePasswordRequestDto
{
    public string CurrentPasswordHash { get; set; } = null!;
    public string NewPasswordHash { get; set; } = null!;
    public string NewKdfSalt { get; set; } = null!;
}
