using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MyPasswordVault.API.Data;
using MyPasswordVault.API.DTOs.Auth;
using MyPasswordVault.API.Models;
using MyPasswordVault.API.Services.Interfaces;
using OtpNet;

namespace MyPasswordVault.API.Services;



public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    private readonly IEmailService _emailService; // Injected email service

    // ASP.NET injects these automatically
    public AuthService(AppDbContext context, IConfiguration config, IEmailService emailService)
    {
        _emailService = emailService;
        _context = context;
        _config = config;
    }

    public async Task<AuthResponseDto> Register(RegisterRequestDto request)
    {
        bool userExists = await _context.Users.AnyAsync(u => u.Email == request.Email || u.Username == request.Username);
        if (userExists)
        {
            throw new InvalidOperationException("An account with that email or username already exists.");
        }
        var emailVerificationToken = GenerateRefreshToken(); 
        var emailVerificationTokenExpiry = DateTime.UtcNow.AddMinutes(15);
        var refreshToken = GenerateRefreshToken();
        var refreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        User newUser = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = request.PasswordHash,
            CreatedAt = DateTime.UtcNow,
            kdfSalt = request.kdfSalt,
            EmailVerificationToken = HashToken(emailVerificationToken),
            EmailVerificationTokenExpiry = emailVerificationTokenExpiry,
            RefreshToken = HashToken(refreshToken),
            RefreshTokenExpiry = refreshTokenExpiry,
            isVerified = false
        };
        var frontendUrl = _config["MailerSend:FrontendBaseUrl"]!;
        var link = $"{frontendUrl}/verify-email?token={Uri.EscapeDataString(emailVerificationToken)}";
        await _emailService.SendVerificationEmailAsync(request.Email, link);
        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

   
        return new AuthResponseDto
        {
            Token = GenerateToken(newUser),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = refreshToken
        };
    }

    public async Task<AuthResponseDto> Login(LoginRequestDto request, string ipAddress, string userAgent)
    {

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null)
        {
            throw new Exception("Invalid email or password!");
        }

        byte[] storedHash = Convert.FromBase64String(user.PasswordHash);
        byte[] providedHash = Convert.FromBase64String(request.PasswordHash);
        bool validPassword = ConstantTimeEquals(storedHash, providedHash);
        if (!validPassword)
        {
            throw new Exception("Invalid email or password!");
        }
        if (!user.isVerified)
        {
            throw new EmailNotVerifiedException("Email not verified");
        }
        if(user.TwoFactorEnabled)
        {
            var pendingToken = GenerateRefreshToken();
            user.PendingTwoFactorToken = HashToken(pendingToken);
            user.PendingTwoFactorTokenExpiry = DateTime.UtcNow.AddMinutes(10);
            await _context.SaveChangesAsync();
            throw new TwoFactorRequiredException(pendingToken);
        }

        var accessToken = GenerateToken(user);
        var refreshToken = GenerateRefreshToken();

        user.RefreshToken = HashToken(refreshToken);
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        await _context.SaveChangesAsync();

        await HandleDeviceCheckAsync(user, ipAddress, userAgent);

        return new AuthResponseDto
        {
            Token = accessToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = refreshToken
        };

    }

   public async Task ResetPassword(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user != null)
        {
            // Cooldown: token expiry is now+15 min, so expiry > now+10 min means
            // the request was made less than 5 minutes ago — silently skip.
            if (user.PasswordResetTokenExpiry.HasValue &&
                user.PasswordResetTokenExpiry.Value > DateTime.UtcNow.AddMinutes(10))
            {
                return;
            }

            var resetToken = GenerateRefreshToken();
            user.PasswordResetToken = HashToken(resetToken);
            user.PasswordResetTokenExpiry = DateTime.UtcNow.AddMinutes(15);
            await _context.SaveChangesAsync();

            var frontendUrl = _config["MailerSend:FrontendBaseUrl"]!;
            var link = $"{frontendUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}";
            await _emailService.SendResetPasswordEmailAsync(email, link);
        }
        // Always silently succeed — anti-enumeration
    }

    public async Task ConfirmResetPassword(ConfirmResetPasswordRequestDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.PasswordResetToken == HashToken(dto.Token) &&
                 u.PasswordResetTokenExpiry > DateTime.UtcNow);
        if (user == null)
            throw new Exception("Invalid or expired reset link.");

        user.PasswordHash = dto.NewPasswordHash;
        user.kdfSalt = dto.NewKdfSalt;
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiry = null;

        // Invalidate all sessions
        user.RefreshToken = string.Empty;
        user.RefreshTokenExpiry = DateTime.MinValue;

        // Delete all vault entries — they are encrypted with the old key and unreadable
        var entries = _context.VaultEntries.Where(v => v.User.Id == user.Id);
        _context.VaultEntries.RemoveRange(entries);

        await _context.SaveChangesAsync();
    }

    public async Task<AuthResponseDto> RefreshToken(string refreshToken)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.RefreshToken == HashToken(refreshToken));
        if (user == null || user.RefreshTokenExpiry < DateTime.UtcNow)
        {
            throw new Exception("Invalid or expired refresh token");
        }

        var newAccessToken = GenerateToken(user);
        var newRefreshToken = GenerateRefreshToken();

        user.RefreshToken = HashToken(newRefreshToken);
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        await _context.SaveChangesAsync();

        return new AuthResponseDto
        {
            Token = newAccessToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = newRefreshToken
        };
    }

    public Task<string> GetSalt(string email)
    {
        var user = _context.Users.FirstOrDefault(u => u.Email == email);
        if (user != null)
            return Task.FromResult(user.kdfSalt);

        var key = Encoding.UTF8.GetBytes(_config["Jwt:PrivateKey"]!);
        var data = Encoding.UTF8.GetBytes(email);
        var fakeSalt = Convert.ToBase64String(HMACSHA256.HashData(key, data));
        return Task.FromResult(fakeSalt);
    }

    public async Task<TwoFactorSetupDto> GenerateTwoFactorSetup(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) throw new Exception("User not found");

        // Generate a 20-byte random secret
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var secretBase32 = Base32Encoding.ToString(secretBytes);

        // Store it temporarily (not enabled yet — user must confirm with a valid code first)
        user.TwoFactorSecret = EncryptTotpSecret(secretBase32);
        await _context.SaveChangesAsync();

        var issuer = "MyPasswordVault";
        var otpUri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email)}?secret={secretBase32}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";

        return new TwoFactorSetupDto { Secret = secretBase32, QrUri = otpUri };
    }

    public async Task<AuthResponseDto> CompleteTwoFactorLogin(VerifyTwoFactorDto dto, string ipAddress, string userAgent)
    {
        var user = await _context.Users.FirstOrDefaultAsync(
            u => u.PendingTwoFactorToken == HashToken(dto.PendingToken) &&
                 u.PendingTwoFactorTokenExpiry > DateTime.UtcNow);
        if (user == null)
            throw new Exception("Invalid or expired pending token");

        var secretBytes = Base32Encoding.ToBytes(DecryptTotpSecret(user.TwoFactorSecret!));
        var totp = new Totp(secretBytes);
        bool valid = totp.VerifyTotp(dto.Code, out _, new VerificationWindow(1, 1));
        if (!valid) throw new Exception("Invalid 2FA code");

        user.PendingTwoFactorToken = null;
        user.PendingTwoFactorTokenExpiry = null;

        var accessToken = GenerateToken(user);
        var refreshToken = GenerateRefreshToken();
        user.RefreshToken = HashToken(refreshToken);
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        await _context.SaveChangesAsync();

        await HandleDeviceCheckAsync(user, ipAddress, userAgent);

        return new AuthResponseDto
        {
            Token = accessToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = refreshToken
        };
    }

    public async Task<(bool Success, List<string>? BackupCodes)> EnableTwoFactor(int userId, string totpCode)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.TwoFactorSecret))
            throw new Exception("Setup not initiated");

        var secretBytes = Base32Encoding.ToBytes(DecryptTotpSecret(user.TwoFactorSecret));
        var totp = new Totp(secretBytes);
        bool valid = totp.VerifyTotp(totpCode, out _, new VerificationWindow(1, 1));

        if (!valid) return (false, null);

        user.TwoFactorEnabled = true;
        var backupCodes = await GenerateAndStoreBackupCodes(user);
        await _context.SaveChangesAsync();
        return (true, backupCodes);
    }

    public async Task DisableTwoFactor(int userId, string totpCode)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) throw new Exception("User not found");

        var secretBytes = Base32Encoding.ToBytes(DecryptTotpSecret(user.TwoFactorSecret!));
        var totp = new Totp(secretBytes);
        bool valid = totp.VerifyTotp(totpCode, out _, new VerificationWindow(1, 1));

        if (!valid) throw new Exception("Invalid code");

        user.TwoFactorEnabled = false;
        user.TwoFactorSecret = null;
        await _context.SaveChangesAsync();
    }

    public async Task<AuthResponseDto> UseBackupCode(string pendingToken, string code, string ipAddress, string userAgent)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(
                u => u.PendingTwoFactorToken == HashToken(pendingToken) &&
                     u.PendingTwoFactorTokenExpiry > DateTime.UtcNow);
        if (user == null)
            throw new Exception("Invalid or expired pending token");

        var hashedCode = HashToken(code.Trim().ToUpperInvariant());
        var backupCode = await _context.TwoFactorBackupCodes
            .FirstOrDefaultAsync(c => c.UserId == user.Id && c.CodeHash == hashedCode && !c.IsUsed);
        if (backupCode == null)
            throw new Exception("Invalid or already-used backup code");

        backupCode.IsUsed = true;
        backupCode.UsedAt = DateTime.UtcNow;
        user.PendingTwoFactorToken = null;
        user.PendingTwoFactorTokenExpiry = null;

        var accessToken = GenerateToken(user);
        var refreshToken = GenerateRefreshToken();
        user.RefreshToken = HashToken(refreshToken);
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        await _context.SaveChangesAsync();

        await HandleDeviceCheckAsync(user, ipAddress, userAgent);

        return new AuthResponseDto
        {
            Token = accessToken,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = refreshToken
        };
    }

    public async Task<List<string>> RegenerateBackupCodes(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null || !user.TwoFactorEnabled)
            throw new Exception("2FA is not enabled");

        var codes = await GenerateAndStoreBackupCodes(user);
        await _context.SaveChangesAsync();
        return codes;
    }

    private async Task<List<string>> GenerateAndStoreBackupCodes(User user)
    {
        // Remove any existing unused codes for this user first
        var existing = _context.TwoFactorBackupCodes.Where(c => c.UserId == user.Id);
        _context.TwoFactorBackupCodes.RemoveRange(existing);

        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var plainCodes = new List<string>();
        for (int i = 0; i < 8; i++)
        {
            var bytes = new byte[6];
            RandomNumberGenerator.Fill(bytes);
            var code = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
            plainCodes.Add(code);
            _context.TwoFactorBackupCodes.Add(new TwoFactorBackupCode
            {
                UserId = user.Id,
                CodeHash = HashToken(code),
                IsUsed = false
            });
        }
        return plainCodes;
    }

    public Task RevokeRefreshToken(string refreshToken)
    {
        var user = _context.Users.FirstOrDefault(u => u.RefreshToken == HashToken(refreshToken));
        if (user != null)
        {
            user.RefreshToken = string.Empty;
            user.RefreshTokenExpiry = DateTime.MinValue;
            return _context.SaveChangesAsync();
        }
        return Task.CompletedTask;
    }

    public async Task VerifyEmail(string token)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.EmailVerificationToken == HashToken(token));
        if (user != null && user.EmailVerificationTokenExpiry > DateTime.UtcNow)
        {
            user.isVerified = true;
            user.EmailVerificationToken = string.Empty;
            user.EmailVerificationTokenExpiry = DateTime.MinValue;
            await _context.SaveChangesAsync();
        }
    }

    public async Task ResendVerificationEmail(string email)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user != null && !user.isVerified)
        {
            var emailVerificationToken = GenerateRefreshToken(); 
            var emailVerificationTokenExpiry = DateTime.UtcNow.AddMinutes(15);
            user.EmailVerificationToken = HashToken(emailVerificationToken);
            user.EmailVerificationTokenExpiry = emailVerificationTokenExpiry;
            await _context.SaveChangesAsync();

            var frontendUrl = _config["MailerSend:FrontendBaseUrl"]!;
            var link = $"{frontendUrl}/verify-email?token={Uri.EscapeDataString(emailVerificationToken)}";
            await _emailService.SendVerificationEmailAsync(email, link);
        }
    }

    private async Task HandleDeviceCheckAsync(User user, string ipAddress, string userAgent)
    {
        var uaDisplay = userAgent.Length > 200 ? userAgent[..200] : userAgent;
        // Use the raw IP in the fingerprint hash for accuracy, but store/display only the anonymized form
        var rawFingerprint = $"{ipAddress}|{userAgent}";
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(rawFingerprint)));
        var anonIp = AnonymizeIp(ipAddress);

        var hasAnyDevice = await _context.KnownDevices.AnyAsync(d => d.UserId == user.Id);
        var existing = await _context.KnownDevices
            .FirstOrDefaultAsync(d => d.UserId == user.Id && d.DeviceHash == hash);

        if (existing != null)
        {
            existing.LastSeenAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return;
        }

        // New fingerprint — register it
        _context.KnownDevices.Add(new KnownDevice
        {
            UserId = user.Id,
            DeviceHash = hash,
            IpAddress = anonIp,
            UserAgentDisplay = uaDisplay,
            FirstSeenAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        // Only alert if the user already had at least one known device (first login = no alert)
        if (hasAnyDevice)
        {
            try
            {
                await _emailService.SendNewLoginAlertAsync(user.Email, anonIp, uaDisplay, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                // Non-fatal: log and swallow so login still succeeds
                Console.Error.WriteLine($"Login alert email failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Anonymizes an IP address before storage:
    /// IPv4 — zeroes the last octet   (192.168.1.42  → 192.168.1.0)
    /// IPv6 — keeps the first 3 groups (2001:db8:85a3::1 → 2001:db8:85a3::)
    /// </summary>
    private static string AnonymizeIp(string ipAddress)
    {
        if (System.Net.IPAddress.TryParse(ipAddress, out var parsed))
        {
            if (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // IPv4: zero out last octet
                var parts = ipAddress.Split('.');
                parts[3] = "0";
                return string.Join('.', parts);
            }
            else if (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // IPv6: expand then keep first 3 groups
                var expanded = parsed.ToString(); // normalized form
                var groups = expanded.Split(':');
                return string.Join(':', groups.Take(3)) + "::";
            }
        }
        return "unknown";
    }

    private string GenerateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var rsa = RSA.Create();
        var privateKeyBytes = Convert.FromBase64String(_config["Jwt:PrivateKey"]!.Trim());
        rsa.ImportPkcs8PrivateKey(privateKeyBytes, out _);

        var creds = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    private string GenerateRefreshToken()
    {
        var randomNumber = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }
    }
    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private string EncryptTotpSecret(string plaintext)
    {
        var key = Convert.FromBase64String(_config["Security:TotpEncryptionKey"]!);
        var iv = new byte[12];
        RandomNumberGenerator.Fill(iv);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, plaintextBytes, ciphertext, tag);
        return $"{Convert.ToBase64String(iv)}:{Convert.ToBase64String(ciphertext)}:{Convert.ToBase64String(tag)}";
    }

    private string DecryptTotpSecret(string encrypted)
    {
        // Base32 uses only A-Z0-9, so ':' unambiguously marks an encrypted value.
        // Legacy plaintext secrets (no ':') are returned as-is during migration.
        if (!encrypted.Contains(':')) return encrypted;
        var parts = encrypted.Split(':', 3);
        if (parts.Length != 3) throw new InvalidOperationException("Invalid TOTP secret format.");
        var key = Convert.FromBase64String(_config["Security:TotpEncryptionKey"]!);
        var iv = Convert.FromBase64String(parts[0]);
        var ciphertext = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(iv, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}