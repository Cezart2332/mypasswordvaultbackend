namespace MyPasswordVault.API.Services.Interfaces;
public interface IEmailService
{
    Task SendVerificationEmailAsync(string toEmail, string verificationLink);
    Task SendResetPasswordEmailAsync(string toEmail, string resetLink);
    Task SendNewLoginAlertAsync(string toEmail, string ipAddress, string userAgent, DateTime loginTime);
}