namespace MyPasswordVault.API.Models;
public class VaultEntry
{
    public int Id { get; set; }
    public User User { get; set; } = null!;
    // All sensitive fields (title, username, url, notes, category, password)
    // are encrypted client-side as a single AES-GCM JSON blob.
    public string EncryptedData { get; set; } = null!;
    public string DataIv { get; set; } = null!;
    public bool IsFavorite { get; set; } = false;
    public DateTime CreatedAt { get; set; }
}