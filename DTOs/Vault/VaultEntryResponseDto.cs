namespace MyPasswordVault.API.DTOs.Vault;

public class VaultEntryResponseDto
{
    public int Id { get; set; }
    public string EncryptedData { get; set; } = null!;
    public string DataIv { get; set; } = null!;
    public bool IsFavorite { get; set; }
    public DateTime CreatedAt { get; set; }
}