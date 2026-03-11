

using MyPasswordVault.API.Models;

namespace MyPasswordVault.API.Services.Interfaces;

public interface IUserService
{
    Task ChangePassword(int userId, string currentPasswordHash, string newPasswordHash, string newKdfSalt);
    Task InitiateEmailChange(int userId, string newEmail, string passwordHash);
    Task ConfirmEmailChange(string token);
    Task<User> DeleteAccount(int userId, string password);
}