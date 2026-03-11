namespace MyPasswordVault.API.DTOs.Auth;
public class TwoFactorSetupDto
{
    public string Secret { get; set; } = null!;
    public string QrUri { get; set; } = null!;
}
