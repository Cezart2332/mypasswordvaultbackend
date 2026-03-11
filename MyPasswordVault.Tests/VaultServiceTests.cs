using Microsoft.EntityFrameworkCore;
using MyPasswordVault.API.Data;
using MyPasswordVault.API.DTOs.Vault;
using MyPasswordVault.API.Models;
using MyPasswordVault.API.Services;

namespace MyPasswordVault.Tests;

public class VaultServiceTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static VaultService CreateService(AppDbContext ctx) =>
        new(ctx, TestFactory.CreateConfig());

    private static async Task<User> SeedUser(AppDbContext ctx, string username = "alice", string email = "alice@example.com")
    {
        var user = new User
        {
            Username = username, Email = email,
            PasswordHash = TestFactory.FakePasswordHash(), kdfSalt = "salt",
            isVerified = true
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    private static VaultEntryRequestDto FakeRequest(
        string data = "encryptedData", string iv = "someIv", bool fav = false) =>
        new() { EncryptedData = data, DataIv = iv, IsFavorite = fav };

    // ─── GetVaultItems ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVaultItems_ReturnsOnlyItemsOwnedByUser()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var alice = await SeedUser(ctx, "alice", "alice@example.com");
        var bob   = await SeedUser(ctx, "bob", "bob@example.com");

        ctx.VaultEntries.AddRange(
            new VaultEntry { User = alice, EncryptedData = "alice-enc", DataIv = "iv1" },
            new VaultEntry { User = alice, EncryptedData = "alice-enc2", DataIv = "iv2" },
            new VaultEntry { User = bob,   EncryptedData = "bob-enc", DataIv = "iv3" }
        );
        await ctx.SaveChangesAsync();

        var items = await svc.GetVaultItems(alice.Id);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Contains("alice", i.EncryptedData));
    }

    [Fact]
    public async Task GetVaultItems_NoItems_ReturnsEmptyList()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var user = await SeedUser(ctx);

        var items = await svc.GetVaultItems(user.Id);

        Assert.Empty(items);
    }

    // ─── AddVaultItem ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddVaultItem_ValidUser_ReturnsCreatedEntry()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var user = await SeedUser(ctx);

        var result = await svc.AddVaultItem(user.Id, FakeRequest("myData", "myIv", true));

        Assert.NotEqual(0, result.Id);
        Assert.Equal("myData", result.EncryptedData);
        Assert.Equal("myIv", result.DataIv);
        Assert.True(result.IsFavorite);
    }

    [Fact]
    public async Task AddVaultItem_ValidUser_PersistsToDatabase()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var user = await SeedUser(ctx);

        await svc.AddVaultItem(user.Id, FakeRequest());

        Assert.Single(await ctx.VaultEntries.ToListAsync());
    }

    [Fact]
    public async Task AddVaultItem_NonexistentUser_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);

        await Assert.ThrowsAsync<Exception>(() => svc.AddVaultItem(999, FakeRequest()));
    }

    // ─── DeleteVaultItem ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteVaultItem_ValidOwner_RemovesEntry()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var user = await SeedUser(ctx);
        var entry = new VaultEntry { User = user, EncryptedData = "enc", DataIv = "iv" };
        ctx.VaultEntries.Add(entry);
        await ctx.SaveChangesAsync();

        await svc.DeleteVaultItem(user.Id, entry.Id);

        Assert.Empty(await ctx.VaultEntries.ToListAsync());
    }

    [Fact]
    public async Task DeleteVaultItem_EntryBelongsToOtherUser_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var alice = await SeedUser(ctx, "alice", "alice@example.com");
        var bob   = await SeedUser(ctx, "bob", "bob@example.com");

        var aliceEntry = new VaultEntry { User = alice, EncryptedData = "enc", DataIv = "iv" };
        ctx.VaultEntries.Add(aliceEntry);
        await ctx.SaveChangesAsync();

        // Bob tries to delete Alice's entry
        await Assert.ThrowsAsync<Exception>(() => svc.DeleteVaultItem(bob.Id, aliceEntry.Id));
    }

    [Fact]
    public async Task DeleteVaultItem_NonexistentEntry_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var user = await SeedUser(ctx);

        await Assert.ThrowsAsync<Exception>(() => svc.DeleteVaultItem(user.Id, 9999));
    }

    // ─── EditVaultItems ───────────────────────────────────────────────────────

    [Fact]
    public async Task EditVaultItems_ValidOwner_UpdatesAndReturnsMutatedEntry()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var user = await SeedUser(ctx);
        var entry = new VaultEntry { User = user, EncryptedData = "old", DataIv = "oldIv", IsFavorite = false };
        ctx.VaultEntries.Add(entry);
        await ctx.SaveChangesAsync();

        var result = await svc.EditVaultItems(user.Id, entry.Id, FakeRequest("new", "newIv", true));

        Assert.Equal("new", result.EncryptedData);
        Assert.Equal("newIv", result.DataIv);
        Assert.True(result.IsFavorite);

        // Verify the DB was updated
        var persisted = await ctx.VaultEntries.FirstAsync();
        Assert.Equal("new", persisted.EncryptedData);
    }

    [Fact]
    public async Task EditVaultItems_EntryBelongsToOtherUser_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var alice = await SeedUser(ctx, "alice", "alice@example.com");
        var bob   = await SeedUser(ctx, "bob", "bob@example.com");

        var aliceEntry = new VaultEntry { User = alice, EncryptedData = "enc", DataIv = "iv" };
        ctx.VaultEntries.Add(aliceEntry);
        await ctx.SaveChangesAsync();

        // Bob tries to edit Alice's entry
        await Assert.ThrowsAsync<Exception>(() =>
            svc.EditVaultItems(bob.Id, aliceEntry.Id, FakeRequest("hacked", "iv")));
    }

    [Fact]
    public async Task EditVaultItems_NonexistentEntry_ThrowsException()
    {
        using var ctx = TestFactory.CreateContext();
        var svc = CreateService(ctx);
        var user = await SeedUser(ctx);

        await Assert.ThrowsAsync<Exception>(() =>
            svc.EditVaultItems(user.Id, 9999, FakeRequest()));
    }
}
