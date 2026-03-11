using MyPasswordVault.API.DTOs.Auth;

namespace MyPasswordVault.API.Services.Interfaces;
public interface IAuthService
{
    Task<AuthResponseDto> Register(RegisterRequestDto request);
    Task<AuthResponseDto> Login(LoginRequestDto request, string ipAddress, string userAgent);
    Task<string> GetSalt(string email);
    Task<AuthResponseDto> RefreshToken(string refreshToken);
    Task VerifyEmail(string token);
    Task ResendVerificationEmail(string email);
    Task RevokeRefreshToken(string refreshToken);
    Task<TwoFactorSetupDto> GenerateTwoFactorSetup(int userId);
    Task<(bool Success, List<string>? BackupCodes)> EnableTwoFactor(int userId, string totpCode);
    Task DisableTwoFactor(int userId, string totpCode);
    Task<AuthResponseDto> CompleteTwoFactorLogin(VerifyTwoFactorDto dto, string ipAddress, string userAgent);
    Task<AuthResponseDto> UseBackupCode(string pendingToken, string code, string ipAddress, string userAgent);
    Task<List<string>> RegenerateBackupCodes(int userId);
    Task ResetPassword(string email);
    Task ConfirmResetPassword(ConfirmResetPasswordRequestDto dto);
}