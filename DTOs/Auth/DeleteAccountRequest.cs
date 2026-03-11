using System.ComponentModel.DataAnnotations;

namespace MyPasswordVault.API.DTOs.Auth;
public class DeleteAccountRequest
{
    [Required]
    public string Password { get; set; } = null!;
}