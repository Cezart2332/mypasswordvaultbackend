using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MyPasswordVault.API.Data;
using MyPasswordVault.API.DTOs.Vault;
using MyPasswordVault.API.Models;
using MyPasswordVault.API.Services.Interfaces;


namespace MyPasswordVault.API.Services;

public class VaultService : IVaultService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public VaultService(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    public async Task<List<VaultEntryResponseDto>> GetVaultItems(int userId)
    {
        return await _context.VaultEntries
            .Where(e => e.User.Id == userId)
            .Select(e => new VaultEntryResponseDto
            {
                Id = e.Id,
                EncryptedData = e.EncryptedData,
                DataIv = e.DataIv,
                IsFavorite = e.IsFavorite,
                CreatedAt = e.CreatedAt,
            })
            .ToListAsync();
    }

    public async Task<VaultEntryResponseDto> AddVaultItem(int userId, VaultEntryRequestDto request)
    {
        User? user = await _context.Users.Where(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null)
        {
            throw new Exception("User not found");
        }

        VaultEntry entry = new VaultEntry
        {
            User = user,
            EncryptedData = request.EncryptedData,
            DataIv = request.DataIv,
            IsFavorite = request.IsFavorite,
            CreatedAt = DateTime.UtcNow,
        };

        _context.VaultEntries.Add(entry);
        await _context.SaveChangesAsync();

        return new VaultEntryResponseDto
        {
            Id = entry.Id,
            EncryptedData = entry.EncryptedData,
            DataIv = entry.DataIv,
            IsFavorite = entry.IsFavorite,
            CreatedAt = entry.CreatedAt,
        };
    }

    public async Task DeleteVaultItem(int userId, int itemId)
    {
        var entry = await _context.VaultEntries
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Id == itemId && e.User.Id == userId);
        if (entry == null)
            throw new Exception("Vault entry not found");
        _context.VaultEntries.Remove(entry);
        await _context.SaveChangesAsync();
    }

    public async Task<VaultEntryResponseDto> EditVaultItems(int userId, int itemId, VaultEntryRequestDto request)
    {
        var entry = await _context.VaultEntries
            .Include(e => e.User)
            .FirstOrDefaultAsync(e => e.Id == itemId && e.User.Id == userId);
        if (entry == null)
            throw new Exception("Vault entry not found");

        entry.EncryptedData = request.EncryptedData;
        entry.DataIv = request.DataIv;
        entry.IsFavorite = request.IsFavorite;

        await _context.SaveChangesAsync();

        return new VaultEntryResponseDto
        {
            Id = entry.Id,
            EncryptedData = entry.EncryptedData,
            DataIv = entry.DataIv,
            IsFavorite = entry.IsFavorite,
            CreatedAt = entry.CreatedAt,
        };
    }
}