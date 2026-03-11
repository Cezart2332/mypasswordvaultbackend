using System.ComponentModel.DataAnnotations;

namespace MyPasswordVault.API.DTOs.Auth;
public class VerifyTwoFactorDto
{
    [Required]
    public string PendingToken { get; set; } = null!;
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = null!;
}