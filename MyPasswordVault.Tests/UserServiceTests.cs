using Microsoft.EntityFrameworkCore;
using Moq;
using MyPasswordVault.API.Data;
using MyPasswordVault.API.Models;
using MyPasswordVault.API.Services;
using MyPasswordVault.API.Services.Interfaces;

namespace MyPasswordVault.Tests;

public class UserServiceTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static UserService CreateService(AppDbContext ctx, IEmailService? email = null) =>
        new(ctx, TestFactory.CreateConfig(), email ?? TestFactory.CreateEmailServiceMock());

    private static async Task<(User user, string plainHash)> SeedVerifiedUser(
        AppDbContext ctx, string username = "alice", string email = "alice@example.com")
    {
        var hash = TestFactory.FakePasswordHash();
        var user = new User
        {
            Username = username, Email = email,
            PasswordHash = hash, kdfSalt = "salt",
            isVerified = true
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return (user, hash);
    }

    // ─── ChangePassword ───────────────────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_ValidCredentials_UpdatesPasswordAndWipesVault()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var (user, oldHash) = await SeedVerifiedUser(ctx);

        // Give the user a vault entry
        ctx.VaultEntries.Add(new VaultEntry { User = user, EncryptedData = "enc", DataIv = "iv" });
        await ctx.SaveChangesAsync();

        var newHash = TestFactory.FakePasswordHash();
        await svc.ChangePassword(user.Id, oldHash, newHash, "newsalt");

        var updated = await ctx.Users.FirstAsync();
        Assert.Equal(newHash, updated.PasswordHash);
        Assert.Empty(await ctx.VaultEntries.ToListAsync());
    }

    [Fact]
    public async Task ChangePassword_ValidCredentials_InvalidatesRefreshToken()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var (user, hash) = await SeedVerifiedUser(ctx);

        // Simulate an active session
        user.RefreshToken = "someHash";
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(7);
        await ctx.SaveChangesAsync();

        await svc.ChangePassword(user.Id, hash, TestFactory.FakePasswordHash(), "newsalt");

        var updated = await ctx.Users.FirstAsync();
        Assert.Equal(string.Empty, updated.RefreshToken);
        Assert.Equal(DateTime.MinValue, updated.RefreshTokenExpiry);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var (user, _) = await SeedVerifiedUser(ctx);

        await Assert.ThrowsAsync<Exception>(() =>
            svc.ChangePassword(user.Id, TestFactory.FakePasswordHash(), TestFactory.FakePasswordHash(), "newsalt"));
    }

    [Fact]
    public async Task ChangePassword_UserNotFound_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);

        await Assert.ThrowsAsync<Exception>(() =>
            svc.ChangePassword(999, TestFactory.FakePasswordHash(), TestFactory.FakePasswordHash(), "salt"));
    }

    // ─── InitiateEmailChange ──────────────────────────────────────────────────

    [Fact]
    public async Task InitiateEmailChange_ValidCredentials_StoresPendingTokenAndSendsEmail()
    {
        using var ctx = TestFactory.CreateContext();
        var emailMock = new Mock<IEmailService>();
        var svc = CreateService(ctx, emailMock.Object);
        var (user, hash) = await SeedVerifiedUser(ctx);

        await svc.InitiateEmailChange(user.Id, "new@example.com", hash);

        var updated = await ctx.Users.FirstAsync();
        Assert.Equal("new@example.com", updated.PendingEmail);
        Assert.NotNull(updated.PendingEmailToken);

        emailMock.Verify(
            e => e.SendVerificationEmailAsync("new@example.com", It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task InitiateEmailChange_WrongPassword_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var (user, _) = await SeedVerifiedUser(ctx);

        await Assert.ThrowsAsync<Exception>(() =>
            svc.InitiateEmailChange(user.Id, "new@example.com", TestFactory.FakePasswordHash()));
    }

    [Fact]
    public async Task InitiateEmailChange_EmailAlreadyTakenByOtherUser_ThrowsInvalidOperationException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var (user, hash) = await SeedVerifiedUser(ctx, "alice", "alice@example.com");

        // Seed a second user with the target e-mail
        ctx.Users.Add(new User
        {
            Username = "bob", Email = "bob@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt2",
            isVerified = true
        });
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.InitiateEmailChange(user.Id, "bob@example.com", hash));
    }

    // ─── ConfirmEmailChange ───────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmEmailChange_ValidToken_UpdatesEmail()
    {
        using var ctx = TestFactory.CreateContext();
        var emailMock = new Mock<IEmailService>();
        string? capturedLink = null;
        emailMock
            .Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, link) => capturedLink = link)
            .Returns(Task.CompletedTask);

        var svc = CreateService(ctx, emailMock.Object);
        var (user, hash) = await SeedVerifiedUser(ctx);

        await svc.InitiateEmailChange(user.Id, "new@example.com", hash);

        var rawToken = Uri.UnescapeDataString(capturedLink!.Split("token=")[1]);
        await svc.ConfirmEmailChange(rawToken);

        var updated = await ctx.Users.FirstAsync();
        Assert.Equal("new@example.com", updated.Email);
        Assert.Null(updated.PendingEmail);
        Assert.Null(updated.PendingEmailToken);
    }

    [Fact]
    public async Task ConfirmEmailChange_ExpiredToken_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        const string rawToken = "expiredchangetoken";

        ctx.Users.Add(new User
        {
            Username = "alice", Email = "alice@example.com",
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt",
            isVerified = true,
            PendingEmail = "new@example.com",
            PendingEmailToken = TestFactory.HashToken(rawToken),
            PendingEmailTokenExpiry = DateTime.UtcNow.AddMinutes(-5)  // expired
        });
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAsync<Exception>(() => svc.ConfirmEmailChange(rawToken));
    }

    [Fact]
    public async Task ConfirmEmailChange_InvalidToken_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);

        await Assert.ThrowsAsync<Exception>(() => svc.ConfirmEmailChange("totallyWrongToken"));
    }

    // ─── DeleteAccount ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAccount_ValidCredentials_RemovesUserAndVaultEntries()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var (user, hash) = await SeedVerifiedUser(ctx);
        ctx.VaultEntries.Add(new VaultEntry { User = user, EncryptedData = "enc", DataIv = "iv" });
        await ctx.SaveChangesAsync();

        await svc.DeleteAccount(user.Id, hash);

        Assert.Empty(await ctx.Users.ToListAsync());
        Assert.Empty(await ctx.VaultEntries.ToListAsync());
    }

    [Fact]
    public async Task DeleteAccount_WrongPassword_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var (user, _) = await SeedVerifiedUser(ctx);

        await Assert.ThrowsAsync<Exception>(() =>
            svc.DeleteAccount(user.Id, TestFactory.FakePasswordHash()));
    }
}
