using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyPasswordVault.API.Data;
using MyPasswordVault.API.Models;
using MyPasswordVault.API.Services.Interfaces;

namespace MyPasswordVault.API.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IEmailService _emailService;

    public UserService(AppDbContext context, IConfiguration config, IEmailService emailService)
    {
        _context = context;
        _config = config;
        _emailService = emailService;
    }

    public async Task ChangePassword(int userId, string currentPasswordHash, string newPasswordHash, string newKdfSalt)
    {
        User? user = await _context.Users.Where(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null)
            throw new Exception("User not found");

        if (!PasswordHashEquals(user.PasswordHash, currentPasswordHash))
            throw new Exception("Current password is incorrect");

        user.PasswordHash = newPasswordHash;
        user.kdfSalt = newKdfSalt;

        // New password = new vault key. Old ciphertext is unreadable.
        var entries = _context.VaultEntries.Where(v => v.User.Id == user.Id);
        _context.VaultEntries.RemoveRange(entries);

        // Invalidate all sessions
        user.RefreshToken = string.Empty;
        user.RefreshTokenExpiry = DateTime.MinValue;

        await _context.SaveChangesAsync();
    }

    public async Task InitiateEmailChange(int userId, string newEmail, string passwordHash)
    {
        User? user = await _context.Users.Where(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null)
            throw new Exception("User not found");

        if (!PasswordHashEquals(user.PasswordHash, passwordHash))
            throw new Exception("Password is incorrect");

        bool emailTaken = await _context.Users.AnyAsync(u => u.Email == newEmail && u.Id != userId);
        if (emailTaken)
            throw new InvalidOperationException("That email is already in use.");

        var token = GenerateToken();
        user.PendingEmail = newEmail;
        user.PendingEmailToken = HashToken(token);
        user.PendingEmailTokenExpiry = DateTime.UtcNow.AddMinutes(15);
        await _context.SaveChangesAsync();

        var frontendUrl = _config["MailerSend:FrontendBaseUrl"]!;
        var link = $"{frontendUrl}/verify-email-change?token={Uri.EscapeDataString(token)}";
        await _emailService.SendVerificationEmailAsync(newEmail, link);
    }

    public async Task ConfirmEmailChange(string token)
    {
        var hashed = HashToken(token);
        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.PendingEmailToken == hashed && u.PendingEmailTokenExpiry > DateTime.UtcNow);
        if (user == null)
            throw new Exception("Invalid or expired token.");

        user.Email = user.PendingEmail!;
        user.PendingEmail = null;
        user.PendingEmailToken = null;
        user.PendingEmailTokenExpiry = null;
        await _context.SaveChangesAsync();
    }
    public async Task<User> DeleteAccount(int userId, string password)
    {
        User? user = await _context.Users.Where(u => u.Id == userId).FirstOrDefaultAsync();
        if (user == null)
        {
            throw new Exception("User not found");
        }

        if (!PasswordHashEquals(user.PasswordHash, password))
        {
            throw new Exception("Password is incorrect");
        }

        // Delete all vault entries
        var entries = _context.VaultEntries.Where(v => v.User.Id == user.Id);
        _context.VaultEntries.RemoveRange(entries);

        // Delete the user
        _context.Users.Remove(user);

        await _context.SaveChangesAsync();
        return user;
    }

    private static string GenerateToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashToken(string token)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static bool PasswordHashEquals(string storedHash, string providedHash)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(storedHash),
                Convert.FromBase64String(providedHash));
        }
        catch
        {
            return false;
        }
    }
}