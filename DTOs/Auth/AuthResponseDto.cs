namespace MyPasswordVault.API.DTOs.Auth;
public class AuthResponseDto
{
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public string RefreshToken { get; set; } = null!;
}