using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyPasswordVault.API.DTOs.Auth;
using MyPasswordVault.API.Models;
using MyPasswordVault.API.Services.Interfaces;
using System.Security.Claims;

namespace MyPasswordVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IUserService _userService;
    private readonly IWebHostEnvironment _env;

    public AuthController(IAuthService authService, IUserService userService, IWebHostEnvironment env)
    {
        _authService = authService;
        _userService = userService;
        _env = env;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
    {
        try
        {
            var result = await _authService.Register(request);
            SetRefreshTokenCookie(result.RefreshToken, DateTime.UtcNow.AddDays(7));
            return Ok(new { token = result.Token, expiresAt = result.ExpiresAt });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        try
        {
            var (ip, ua) = GetClientContext();
            var result = await _authService.Login(request, ip, ua);
            SetRefreshTokenCookie(result.RefreshToken, DateTime.UtcNow.AddDays(7));
            return Ok(new { token = result.Token, expiresAt = result.ExpiresAt });
        }
        catch (TwoFactorRequiredException ex)
        {
            return Ok(new { requiresTwoFactor = true, pendingToken = ex.PendingToken });
        }
    }

    [HttpPost("verify-2fa")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyTwoFactor([FromBody] VerifyTwoFactorDto request)
    {
        var (ip, ua) = GetClientContext();
        var result = await _authService.CompleteTwoFactorLogin(request, ip, ua);
        SetRefreshTokenCookie(result.RefreshToken, DateTime.UtcNow.AddDays(7));
        return Ok(new { token = result.Token, expiresAt = result.ExpiresAt });
    }

    [HttpGet("2fa/setup")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> GetTwoFactorSetup()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var setup = await _authService.GenerateTwoFactorSetup(userId);
        return Ok(setup);
    }

    [HttpPost("2fa/enable")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> EnableTwoFactor([FromBody] EnableDisableTwoFactorDto request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var (success, backupCodes) = await _authService.EnableTwoFactor(userId, request.Code);
        if (!success) return BadRequest(new { message = "Invalid code. Please try again." });
        return Ok(new { message = "Two-factor authentication enabled.", backupCodes });
    }

    [HttpPost("2fa/disable")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> DisableTwoFactor([FromBody] EnableDisableTwoFactorDto request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await _authService.DisableTwoFactor(userId, request.Code);
        return Ok(new { message = "Two-factor authentication disabled." });
    }

    [HttpPost("2fa/use-backup-code")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> UseBackupCode([FromBody] UseBackupCodeDto request)
    {
        var (ip, ua) = GetClientContext();
        var result = await _authService.UseBackupCode(request.PendingToken, request.Code, ip, ua);
        SetRefreshTokenCookie(result.RefreshToken, DateTime.UtcNow.AddDays(7));
        return Ok(new { token = result.Token, expiresAt = result.ExpiresAt });
    }

    [HttpPost("2fa/backup-codes/regenerate")]
    [Authorize]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RegenerateBackupCodes()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var codes = await _authService.RegenerateBackupCodes(userId);
        return Ok(new { backupCodes = codes });
    }
    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> RefreshToken()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new { message = "No refresh token cookie" });

        var result = await _authService.RefreshToken(refreshToken);

        // Rotate: set the new refresh token cookie
        SetRefreshTokenCookie(result.RefreshToken, DateTime.UtcNow.AddDays(7));

        return Ok(new { token = result.Token, expiresAt = result.ExpiresAt });
    }
    [HttpPost("logout")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Logout()
    {
        var refreshToken = Request.Cookies["refreshToken"];
        if (refreshToken != null)
            await _authService.RevokeRefreshToken(refreshToken);

        // ✅ Clear the cookie
        Response.Cookies.Delete("refreshToken");
        return NoContent();
    }
    [HttpPost("get-salt")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> GetSalt([FromBody] GetSaltRequestDto request)
    {
        var salt = await _authService.GetSalt(request.Email);
        return Ok(new { kdfSalt = salt });
    }

    [HttpPost("verify-email")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequestDto request)
    {
        await _authService.VerifyEmail(request.Token);
        return Ok(new { message = "Email verified successfully." });
    }

    [HttpPost("verify-email-change")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyEmailChange([FromBody] VerifyEmailChangeRequestDto request)
    {
        try
        {
            await _userService.ConfirmEmailChange(request.Token);
            return Ok(new { message = "Email updated successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("resend-verification")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequestDto request)
    {
        await _authService.ResendVerificationEmail(request.Email);
        return Ok(new { message = "If that email is registered and unverified, a new link has been sent." });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request)
    {
        await _authService.ResetPassword(request.Email);
        // Always return 200 — don't reveal whether the email exists
        return Ok(new { message = "If that email is registered, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword([FromBody] ConfirmResetPasswordRequestDto request)
    {
        await _authService.ConfirmResetPassword(request);
        return Ok(new { message = "Password reset successfully. Please log in with your new password." });
    }

    private void SetRefreshTokenCookie(string refreshToken, DateTime expiresAt)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Expires = expiresAt
        };
        Response.Cookies.Append("refreshToken", refreshToken, cookieOptions);
    }

    private (string ip, string ua) GetClientContext()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var ua = Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(ua)) ua = "unknown";
        return (ip, ua);
    }
}