namespace MyPasswordVault.API.Models;
public class TwoFactorRequiredException : Exception
{
    public string PendingToken { get; }
    public TwoFactorRequiredException(string pendingToken) : base("Two-factor authentication required")
    {
        PendingToken = pendingToken;
    }
}
