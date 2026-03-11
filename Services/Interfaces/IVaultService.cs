using MyPasswordVault.API.DTOs.Vault;

namespace MyPasswordVault.API.Services.Interfaces;

public interface IVaultService
{
    
    Task<List<VaultEntryResponseDto>> GetVaultItems(int userId);
    Task<VaultEntryResponseDto> AddVaultItem(int userId, VaultEntryRequestDto request);
    Task<VaultEntryResponseDto> EditVaultItems(int userId, int itemId, VaultEntryRequestDto request);
    Task DeleteVaultItem(int userId, int itemId);
}