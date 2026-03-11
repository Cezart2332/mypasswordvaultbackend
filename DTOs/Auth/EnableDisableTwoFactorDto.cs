using System.ComponentModel.DataAnnotations;

namespace MyPasswordVault.API.DTOs.Auth;
public class EnableDisableTwoFactorDto
{
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = null!;
}
