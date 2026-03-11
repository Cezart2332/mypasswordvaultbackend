using System.ComponentModel.DataAnnotations;

namespace MyPasswordVault.API.DTOs.Auth;
public class VerifyEmailRequestDto
{
    [Required]
    public string Token { get; set; } = null!;
}
