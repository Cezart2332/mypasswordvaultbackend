using System.ComponentModel.DataAnnotations;

namespace MyPasswordVault.API.DTOs.Auth;
public class RegisterRequestDto
{
    [Required]
    public string Username { get; set; } = null!;
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;
    [Required]
    [StringLength(100, MinimumLength = 8)]
    public string PasswordHash { get; set; } = null!;
    [Required]
    public string kdfSalt { get; set; } = null!;
}