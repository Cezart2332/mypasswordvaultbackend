using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using MyPasswordVault.API.Data;
using MyPasswordVault.API.Services.Interfaces;

namespace MyPasswordVault.Tests;

/// <summary>
/// Shared helpers for all test classes.
/// </summary>
public static class TestFactory
{
    // One RSA key pair for the entire test run — keeps tests fast.
    private static readonly string _privateKeyBase64;
    private static readonly string _publicKeyBase64;

    static TestFactory()
    {
        using var rsa = RSA.Create(2048);
        _privateKeyBase64 = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        _publicKeyBase64 = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Creates an isolated in-memory AppDbContext for a single test.</summary>
    public static AppDbContext CreateContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>Returns an IConfiguration mock wired with JWT + email settings.</summary>
    public static IConfiguration CreateConfig()
    {
        var mock = new Mock<IConfiguration>();
        mock.Setup(c => c["Jwt:PrivateKey"]).Returns(_privateKeyBase64);
        mock.Setup(c => c["Jwt:PublicKey"]).Returns(_publicKeyBase64);
        mock.Setup(c => c["Jwt:Issuer"]).Returns("TestIssuer");
        mock.Setup(c => c["Jwt:Audience"]).Returns("TestAudience");
        mock.Setup(c => c["MailerSend:FrontendBaseUrl"]).Returns("http://localhost:3000");
        return mock.Object;
    }

    /// <summary>Returns a no-op IEmailService mock.</summary>
    public static IEmailService CreateEmailServiceMock() =>
        new Mock<IEmailService>().Object;

    /// <summary>
    /// Simulates a client-side password hash: random 32 bytes encoded as Base64,
    /// matching the format the real client sends.
    /// </summary>
    public static string FakePasswordHash()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Replicates the SHA-256 token hashing used internally by AuthService / UserService,
    /// so tests can pre-populate hashed tokens directly into the database.
    /// </summary>
    public static string HashToken(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
