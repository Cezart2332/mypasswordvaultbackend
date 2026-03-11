namespace MyPasswordVault.API.DTOs.User;

public class ChangeEmailRequestDto
{
    public string NewEmail { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
}
