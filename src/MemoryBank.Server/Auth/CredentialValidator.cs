using MemoryBank.Core.Auth;

namespace MemoryBank.Server.Auth;

public interface ICredentialValidator
{
    string ConfiguredUsername { get; }
    bool Validate(string username, string password);
}

/// <summary>
/// Reads <c>MEMORYBANK_AUTH_USERNAME</c> and <c>MEMORYBANK_AUTH_PASSWORD_HASH</c> from the environment.
/// </summary>
public sealed class EnvCredentialValidator : ICredentialValidator
{
    public const string UsernameEnvVar = "MEMORYBANK_AUTH_USERNAME";
    public const string PasswordHashEnvVar = "MEMORYBANK_AUTH_PASSWORD_HASH";

    private readonly string _passwordHash;

    public string ConfiguredUsername { get; }

    public EnvCredentialValidator(string username, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("username must be non-empty", nameof(username));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("passwordHash must be non-empty", nameof(passwordHash));
        ConfiguredUsername = username;
        _passwordHash = passwordHash;
    }

    public static EnvCredentialValidator FromEnvironment()
    {
        var username = Environment.GetEnvironmentVariable(UsernameEnvVar);
        var hash = Environment.GetEnvironmentVariable(PasswordHashEnvVar);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(hash))
        {
            throw new InvalidOperationException(
                $"HTTP transport requires {UsernameEnvVar} and {PasswordHashEnvVar} environment variables. " +
                "Generate a password hash with: dotnet run --project src/MemoryBank.Server -- --hash-password <password>");
        }
        return new EnvCredentialValidator(username, hash);
    }

    public bool Validate(string username, string password)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return false;
        if (username != ConfiguredUsername) return false;
        return PasswordHasher.Verify(password, _passwordHash);
    }
}
