using Microsoft.AspNetCore.Mvc;
using MyPasswordVault.API.DTOs.Auth;
using MyPasswordVault.API.DTOs.User;
using MyPasswordVault.API.Services.Interfaces;
using MyPasswordVault.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using MyPasswordVault.API.Data;

namespace MyPasswordVault.API.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;

    public UserController(IUserService userService, AppDbContext context, IWebHostEnvironment env)
    {
        _userService = userService;
        _context = context;
        _env = env;
    }

    [HttpGet("me")]
    [EnableRateLimiting("user")]
    public IActionResult GetUser()
    {
        return Ok(new
        {
            Id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0"),
            Username = User.FindFirstValue(ClaimTypes.Name) ?? "Unknown",
            Email = User.FindFirstValue(ClaimTypes.Email) ?? "Unknown",
            TwoFactorEnabled = _context.Users
                .Where(u => u.Id == int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0"))
                .Select(u => u.TwoFactorEnabled)
                .FirstOrDefault()
        });
    }
    [HttpPost("change-password")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
            await _userService.ChangePassword(userId, request.CurrentPasswordHash, request.NewPasswordHash, request.NewKdfSalt);
            // Session is now invalid — force client to re-login
            Response.Cookies.Delete("refreshToken", new CookieOptions
            {
                HttpOnly = true,
                Secure = !_env.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
            });
            return Ok(new { message = "Password changed. Vault data cleared. Please log in again." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("change-email")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequestDto request)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
            await _userService.InitiateEmailChange(userId, request.NewEmail, request.PasswordHash);
            return Ok(new { message = "Verification email sent to the new address." });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("delete-account")]
    [EnableRateLimiting("user")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
            await _userService.DeleteAccount(userId, request.Password);
            Response.Cookies.Delete("refreshToken", new CookieOptions
            {
                HttpOnly = true,
                Secure = !_env.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
            });
            return Ok(new { Message = "Account deleted successfully." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}