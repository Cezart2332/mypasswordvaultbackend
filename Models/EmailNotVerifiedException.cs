namespace MyPasswordVault.API.Models;
public class EmailNotVerifiedException : Exception
{
    public EmailNotVerifiedException(string message) : base(message) { }
}
