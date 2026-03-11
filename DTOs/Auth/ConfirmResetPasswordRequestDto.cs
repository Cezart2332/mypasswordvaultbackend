using System.ComponentModel.DataAnnotations;

namespace MyPasswordVault.API.DTOs.Auth;
public class ConfirmResetPasswordRequestDto
{
    [Required]
    public string Token { get; set; } = null!;
    [Required]
    public string NewPasswordHash { get; set; } = null!;
    [Required]
    public string NewKdfSalt { get; set; } = null!;
}
