using System.ComponentModel.DataAnnotations;

namespace MyPasswordVault.API.DTOs.Vault;

public class VaultEntryRequestDto
{
    [Required]
    public string EncryptedData { get; set; } = null!;
    [Required]
    public string DataIv { get; set; } = null!;
    public bool IsFavorite { get; set; } = false;
}