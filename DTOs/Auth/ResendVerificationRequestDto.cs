using System.ComponentModel.DataAnnotations;

namespace MyPasswordVault.API.DTOs.Auth;
public class ResendVerificationRequestDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;
}
