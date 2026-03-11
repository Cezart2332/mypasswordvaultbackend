using Microsoft.EntityFrameworkCore;
using Moq;
using MyPasswordVault.API.Data;
using MyPasswordVault.API.DTOs.Auth;
using MyPasswordVault.API.Models;
using MyPasswordVault.API.Services;
using MyPasswordVault.API.Services.Interfaces;

namespace MyPasswordVault.Tests;

public class AuthServiceTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static AuthService CreateService(AppDbContext ctx, IEmailService? email = null) =>
        new(ctx, TestFactory.CreateConfig(), email ?? TestFactory.CreateEmailServiceMock());

    /// <summary>Seeds a basic verified user and returns their plain-text password hash.</summary>
    private static async Task<(MyPasswordVault.API.Models.User user, string plainHash)> SeedVerifiedUser(
        AppDbContext ctx,
        string username = "alice",
        string email = "alice@example.com")
    {
        var hash = TestFactory.FakePasswordHash();
        var user = new MyPasswordVault.API.Models.User
        {
            Username = username, Email = email,
            PasswordHash = hash, kdfSalt = "salt",
            isVerified = true
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return (user, hash);
    }

    // ─── Register ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_NewUser_ReturnsAccessAndRefreshTokens()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);

        var result = await svc.Register(new RegisterRequestDto
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt"
        });

        Assert.NotEmpty(result.Token);
        Assert.NotEmpty(result.RefreshToken);
        Assert.True(result.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ThrowsInvalidOperationException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);

        await svc.Register(new RegisterRequestDto
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.Register(new RegisterRequestDto
        {
            Username = "alice2", Email = "alice@example.com",   // same email
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt"
        }));
    }

    [Fact]
    public async Task Register_DuplicateUsername_ThrowsInvalidOperationException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);

        await svc.Register(new RegisterRequestDto
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.Register(new RegisterRequestDto
        {
            Username = "alice", Email = "other@example.com",    // same username
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt"
        }));
    }

    [Fact]
    public async Task Register_NewUser_SendsVerificationEmail()
    {
        using var ctx = TestFactory.CreateContext();
        var emailMock = new Mock<IEmailService>();
        var svc = CreateService(ctx, emailMock.Object);

        await svc.Register(new RegisterRequestDto
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt"
        });

        emailMock.Verify(
            e => e.SendVerificationEmailAsync("alice@example.com", It.IsAny<string>()),
            Times.Once);
    }

    // ─── VerifyEmail ──────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyEmail_ValidToken_SetsIsVerified()
    {
        using var ctx = TestFactory.CreateContext();
        string? capturedLink = null;
        var emailMock = new Mock<IEmailService>();
        emailMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, link) => capturedLink = link)
            .Returns(Task.CompletedTask);

        var svc = CreateService(ctx, emailMock.Object);

        await svc.Register(new RegisterRequestDto
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt"
        });

        // Extract plain token from link: …/verify-email?token=<token>
        var token = Uri.UnescapeDataString(capturedLink!.Split("token=")[1]);
        await svc.VerifyEmail(token);

        var user = await ctx.Users.FirstAsync();
        Assert.True(user.isVerified);
    }

    // ─── Login ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokens()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var (_, hash) = await SeedVerifiedUser(ctx);

        var result = await svc.Login(new LoginRequestDto
        {
            Email = "alice@example.com", PasswordHash = hash
        });

        Assert.NotEmpty(result.Token);
        Assert.NotEmpty(result.RefreshToken);
    }

    [Fact]
    public async Task Login_WrongPassword_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        await SeedVerifiedUser(ctx);

        await Assert.ThrowsAsync<Exception>(() => svc.Login(new LoginRequestDto
        {
            Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash() // different hash
        }));
    }

    [Fact]
    public async Task Login_NonexistentUser_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);

        await Assert.ThrowsAsync<Exception>(() => svc.Login(new LoginRequestDto
        {
            Email = "nobody@example.com", PasswordHash = TestFactory.FakePasswordHash()
        }));
    }

    [Fact]
    public async Task Login_UnverifiedEmail_ThrowsEmailNotVerifiedException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var hash = TestFactory.FakePasswordHash();
        ctx.Users.Add(new MyPasswordVault.API.Models.User
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = hash, kdfSalt = "salt", isVerified = false
        });
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<EmailNotVerifiedException>(() => svc.Login(new LoginRequestDto
        {
            Email = "alice@example.com", PasswordHash = hash
        }));
    }

    [Fact]
    public async Task Login_TwoFactorEnabled_ThrowsTwoFactorRequiredException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var hash = TestFactory.FakePasswordHash();
        ctx.Users.Add(new MyPasswordVault.API.Models.User
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = hash, kdfSalt = "salt",
            isVerified = true, TwoFactorEnabled = true,
            TwoFactorSecret = "JBSWY3DPEHPK3PXP"
        });
        await ctx.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<TwoFactorRequiredException>(() => svc.Login(new LoginRequestDto
        {
            Email = "alice@example.com", PasswordHash = hash
        }));

        Assert.NotEmpty(ex.PendingToken);
    }

    // ─── RefreshToken ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ValidToken_ReturnsNewRotatedTokens()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var reg = await svc.Register(new RegisterRequestDto
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt"
        });

        var result = await svc.RefreshToken(reg.RefreshToken);

        Assert.NotEmpty(result.Token);
        Assert.NotEmpty(result.RefreshToken);
        Assert.NotEqual(reg.RefreshToken, result.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_InvalidToken_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);

        await Assert.ThrowsAsync<Exception>(() => svc.RefreshToken("bogus-token-value"));
    }

    [Fact]
    public async Task RefreshToken_ExpiredToken_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        const string rawToken = "someRawTokenValue==";
        ctx.Users.Add(new MyPasswordVault.API.Models.User
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt",
            isVerified = true,
            RefreshToken = TestFactory.HashToken(rawToken),
            RefreshTokenExpiry = DateTime.UtcNow.AddDays(-1) // already expired
        });
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<Exception>(() => svc.RefreshToken(rawToken));
    }

    // ─── ResetPassword / ConfirmResetPassword ─────────────────────────────────

    [Fact]
    public async Task ResetPassword_ExistingEmail_SendsResetEmail()
    {
        using var ctx = TestFactory.CreateContext();
        var emailMock = new Mock<IEmailService>();
        var svc = CreateService(ctx, emailMock.Object);
        await SeedVerifiedUser(ctx);

        await svc.ResetPassword("alice@example.com");

        emailMock.Verify(
            e => e.SendResetPasswordEmailAsync("alice@example.com", It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task ResetPassword_NonexistentEmail_DoesNotThrowOrLeakInfo()
    {
        // Anti-enumeration: must silently succeed even for unknown e-mails
        using var ctx = TestFactory.CreateContext();
        var emailMock = new Mock<IEmailService>();
        var svc = CreateService(ctx, emailMock.Object);

        var ex = await Record.ExceptionAsync(() => svc.ResetPassword("nobody@example.com"));

        Assert.Null(ex);
        emailMock.Verify(e => e.SendResetPasswordEmailAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmResetPassword_ValidToken_ResetsPasswordAndWipesVault()
    {
        using var ctx = TestFactory.CreateContext();
        var emailMock = new Mock<IEmailService>();
        string? capturedLink = null;
        emailMock
            .Setup(e => e.SendResetPasswordEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, link) => capturedLink = link)
            .Returns(Task.CompletedTask);

        var svc = CreateService(ctx, emailMock.Object);
        var (user, _) = await SeedVerifiedUser(ctx);

        // Add a vault entry so we can verify it gets wiped
        ctx.VaultEntries.Add(new VaultEntry { User = user, EncryptedData = "enc", DataIv = "iv" });
        await ctx.SaveChangesAsync();

        await svc.ResetPassword("alice@example.com");

        var rawToken = Uri.UnescapeDataString(capturedLink!.Split("token=")[1]);
        var newHash = TestFactory.FakePasswordHash();
        await svc.ConfirmResetPassword(new ConfirmResetPasswordRequestDto
        {
            Token = rawToken, NewPasswordHash = newHash, NewKdfSalt = "newsalt"
        });

        var updated = await ctx.Users.FirstAsync();
        Assert.Equal(newHash, updated.PasswordHash);
        Assert.Empty(await ctx.VaultEntries.ToListAsync());
    }

    [Fact]
    public async Task ConfirmResetPassword_InvalidToken_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);

        await Assert.ThrowsAsync<Exception>(() => svc.ConfirmResetPassword(new ConfirmResetPasswordRequestDto
        {
            Token = "invalidtoken", NewPasswordHash = TestFactory.FakePasswordHash(), NewKdfSalt = "salt"
        }));
    }

    // ─── UseBackupCode ────────────────────────────────────────────────────────

    [Fact]
    public async Task UseBackupCode_ValidCode_LogsInAndMarksCodeUsed()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var hash = TestFactory.FakePasswordHash();
        var pendingToken = "pendingToken123";
        var backupCodePlain = "ABCDEF";

        ctx.Users.Add(new MyPasswordVault.API.Models.User
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = hash, kdfSalt = "salt",
            isVerified = true, TwoFactorEnabled = true,
            TwoFactorSecret = "JBSWY3DPEHPK3PXP",
            PendingTwoFactorToken = TestFactory.HashToken(pendingToken),
            PendingTwoFactorTokenExpiry = DateTime.UtcNow.AddMinutes(10)
        });
        await ctx.SaveChangesAsync();

        var user = await ctx.Users.FirstAsync();
        ctx.TwoFactorBackupCodes.Add(new MyPasswordVault.API.Models.TwoFactorBackupCode
        {
            UserId = user.Id,
            CodeHash = TestFactory.HashToken(backupCodePlain.Trim().ToUpperInvariant()),
            IsUsed = false
        });
        await ctx.SaveChangesAsync();

        var result = await svc.UseBackupCode(pendingToken, backupCodePlain);

        Assert.NotEmpty(result.Token);

        var code = await ctx.TwoFactorBackupCodes.FirstAsync();
        Assert.True(code.IsUsed);
    }

    [Fact]
    public async Task UseBackupCode_AlreadyUsedCode_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var pendingToken = "pendingToken456";
        var backupCodePlain = "XYZABC";

        ctx.Users.Add(new MyPasswordVault.API.Models.User
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt",
            isVerified = true, TwoFactorEnabled = true,
            TwoFactorSecret = "JBSWY3DPEHPK3PXP",
            PendingTwoFactorToken = TestFactory.HashToken(pendingToken),
            PendingTwoFactorTokenExpiry = DateTime.UtcNow.AddMinutes(10)
        });
        await ctx.SaveChangesAsync();

        var user = await ctx.Users.FirstAsync();
        ctx.TwoFactorBackupCodes.Add(new MyPasswordVault.API.Models.TwoFactorBackupCode
        {
            UserId = user.Id,
            CodeHash = TestFactory.HashToken(backupCodePlain.Trim().ToUpperInvariant()),
            IsUsed = true  // already used
        });
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<Exception>(() => svc.UseBackupCode(pendingToken, backupCodePlain));
    }

    [Fact]
    public async Task UseBackupCode_ExpiredPendingToken_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var pendingToken = "expiredPending";

        ctx.Users.Add(new MyPasswordVault.API.Models.User
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt",
            isVerified = true, TwoFactorEnabled = true,
            TwoFactorSecret = "JBSWY3DPEHPK3PXP",
            PendingTwoFactorToken = TestFactory.HashToken(pendingToken),
            PendingTwoFactorTokenExpiry = DateTime.UtcNow.AddMinutes(-5) // expired
        });
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<Exception>(() => svc.UseBackupCode(pendingToken, "ANYCODE"));
    }

    // ─── RegenerateBackupCodes ─────────────────────────────────────────────────

    [Fact]
    public async Task RegenerateBackupCodes_TwoFactorEnabled_Returns8NewCodes()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        ctx.Users.Add(new MyPasswordVault.API.Models.User
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt",
            isVerified = true, TwoFactorEnabled = true,
            TwoFactorSecret = "JBSWY3DPEHPK3PXP"
        });
        await ctx.SaveChangesAsync();

        var user = await ctx.Users.FirstAsync();
        var codes = await svc.RegenerateBackupCodes(user.Id);

        Assert.Equal(8, codes.Count);
        Assert.Equal(8, codes.Distinct().Count()); // all unique
    }

    [Fact]
    public async Task RegenerateBackupCodes_TwoFactorDisabled_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        ctx.Users.Add(new MyPasswordVault.API.Models.User
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt",
            isVerified = true, TwoFactorEnabled = false
        });
        await ctx.SaveChangesAsync();

        var user = await ctx.Users.FirstAsync();
        await Assert.ThrowsAsync<Exception>(() => svc.RegenerateBackupCodes(user.Id));
    }

    // ─── RevokeRefreshToken ────────────────────────────────────────────────────

    [Fact]
    public async Task RevokeRefreshToken_ValidToken_ClearsStoredToken()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var reg = await svc.Register(new RegisterRequestDto
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt"
        });

        await svc.RevokeRefreshToken(reg.RefreshToken);

        var user = await ctx.Users.FirstAsync();
        Assert.Equal(string.Empty, user.RefreshToken);
    }
}
