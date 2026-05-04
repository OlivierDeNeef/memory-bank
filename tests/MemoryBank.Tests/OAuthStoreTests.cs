using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using MemoryBank.Core.Auth;
using MemoryBank.Core.Configuration;
using MemoryBank.Core.Storage;

namespace MemoryBank.Tests;

public class OAuthStoreTests : IDisposable
{
    private readonly MemoryBankDb _db;
    private readonly OAuthStore _store;
    private readonly MemoryBankConfiguration _config;

    public OAuthStoreTests()
    {
        _config = new MemoryBankConfiguration();
        _config.Database.Path = Path.Combine(Path.GetTempPath(), $"memorybank_oauth_test_{Guid.NewGuid()}.db");
        _db = new MemoryBankDb(_config, NullLogger<MemoryBankDb>.Instance);
        _store = new OAuthStore(_db, NullLogger<OAuthStore>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_config.Database.Path); } catch { }
        try { File.Delete(_config.Database.Path + "-wal"); } catch { }
        try { File.Delete(_config.Database.Path + "-shm"); } catch { }
    }

    [Fact]
    public void RegisterClient_RoundTrip()
    {
        var client = _store.RegisterClient("test-client", new[] { "https://example.com/cb" });
        var fetched = _store.GetClient(client.ClientId);

        Assert.NotNull(fetched);
        Assert.Equal("test-client", fetched.ClientName);
        Assert.Single(fetched.RedirectUris);
        Assert.Equal("https://example.com/cb", fetched.RedirectUris[0]);
    }

    [Fact]
    public void GetClient_Returns_Null_For_Unknown_Id()
    {
        Assert.Null(_store.GetClient(Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void IssueAuthCode_And_Consume_With_PKCE_Succeeds()
    {
        var client = _store.RegisterClient("c", new[] { "https://example.com/cb" });
        var verifier = "the-quick-brown-fox-jumps-over-the-lazy-dog-12345678901234";
        var challenge = ComputeChallenge(verifier);

        var code = _store.IssueAuthCode(client.ClientId, "https://example.com/cb", challenge, "S256", "alice");
        var consumed = _store.ConsumeAuthCode(code, verifier);

        Assert.NotNull(consumed);
        Assert.Equal("alice", consumed.Username);
        Assert.Equal(client.ClientId, consumed.ClientId);
        Assert.Equal("https://example.com/cb", consumed.RedirectUri);
    }

    [Fact]
    public void ConsumeAuthCode_Is_Single_Use()
    {
        var client = _store.RegisterClient("c", Array.Empty<string>());
        var verifier = "verifier-with-enough-length-to-be-valid-1234567890";
        var code = _store.IssueAuthCode(client.ClientId, "", ComputeChallenge(verifier), "S256", "alice");

        Assert.NotNull(_store.ConsumeAuthCode(code, verifier));
        Assert.Null(_store.ConsumeAuthCode(code, verifier));
    }

    [Fact]
    public void ConsumeAuthCode_With_Wrong_Verifier_Fails()
    {
        var client = _store.RegisterClient("c", Array.Empty<string>());
        var challenge = ComputeChallenge("correct-verifier-with-enough-entropy-to-pass-checks");
        var code = _store.IssueAuthCode(client.ClientId, "", challenge, "S256", "alice");

        Assert.Null(_store.ConsumeAuthCode(code, "wrong-verifier-with-enough-entropy-to-pass"));
        // The code is still consumed (single-use even on failure prevents brute-force)
        Assert.Null(_store.ConsumeAuthCode(code, "correct-verifier-with-enough-entropy-to-pass-checks"));
    }

    [Fact]
    public void ConsumeAuthCode_Without_Verifier_When_Challenge_Set_Fails()
    {
        var client = _store.RegisterClient("c", Array.Empty<string>());
        var code = _store.IssueAuthCode(client.ClientId, "", ComputeChallenge("verifier-1234567890123456789012345"), "S256", "alice");

        Assert.Null(_store.ConsumeAuthCode(code, null));
    }

    [Fact]
    public void ConsumeAuthCode_Returns_Null_For_Unknown_Code()
    {
        Assert.Null(_store.ConsumeAuthCode("nonexistent", "verifier"));
    }

    [Fact]
    public void IssueTokens_And_Validate_Access()
    {
        var client = _store.RegisterClient("c", Array.Empty<string>());
        var pair = _store.IssueTokens(client.ClientId, "alice");

        var validated = _store.ValidateAccessToken(pair.AccessToken);
        Assert.NotNull(validated);
        Assert.Equal("alice", validated.Username);
        Assert.Equal(client.ClientId, validated.ClientId);
        Assert.Equal(pair.RefreshToken, validated.RefreshToken);
    }

    [Fact]
    public void ValidateAccessToken_Returns_Null_For_Unknown()
    {
        Assert.Null(_store.ValidateAccessToken("nonexistent"));
    }

    [Fact]
    public void RefreshTokens_Rotates_And_Invalidates_Old_Pair()
    {
        var client = _store.RegisterClient("c", Array.Empty<string>());
        var pair1 = _store.IssueTokens(client.ClientId, "alice");

        var pair2 = _store.RefreshTokens(pair1.RefreshToken);

        Assert.NotNull(pair2);
        Assert.NotEqual(pair1.AccessToken, pair2.AccessToken);
        Assert.NotEqual(pair1.RefreshToken, pair2.RefreshToken);

        // Old refresh is rejected (cannot be replayed)
        Assert.Null(_store.RefreshTokens(pair1.RefreshToken));
        // Old access token cascade-deleted via FK
        Assert.Null(_store.ValidateAccessToken(pair1.AccessToken));
        // New tokens work
        Assert.NotNull(_store.ValidateAccessToken(pair2.AccessToken));
    }

    [Fact]
    public void RefreshTokens_Returns_Null_For_Unknown_Token()
    {
        Assert.Null(_store.RefreshTokens("nonexistent"));
    }

    [Fact]
    public void RevokeRefreshToken_Invalidates_Both_Sides()
    {
        var client = _store.RegisterClient("c", Array.Empty<string>());
        var pair = _store.IssueTokens(client.ClientId, "alice");

        _store.RevokeRefreshToken(pair.RefreshToken);

        Assert.Null(_store.ValidateAccessToken(pair.AccessToken));
        Assert.Null(_store.RefreshTokens(pair.RefreshToken));
    }

    [Fact]
    public void CleanupExpired_Removes_Stale_Rows_And_Leaves_Live_Ones()
    {
        // Live row
        var client = _store.RegisterClient("c", Array.Empty<string>());
        var live = _store.IssueTokens(client.ClientId, "alice");

        // Stale rows injected directly (we can't fast-forward time in unit tests)
        using (var cmd = _db.CreateCommand("""
            INSERT INTO oauth_authorization_codes (code, client_id, redirect_uri, username, expires_at, created_at)
            VALUES ('stale-code', @client, '', 'u', '2020-01-01T00:00:00.0000000Z', '2020-01-01T00:00:00.0000000Z');
            INSERT INTO oauth_refresh_tokens (token, client_id, username, expires_at, created_at)
            VALUES ('stale-refresh', @client, 'u', '2020-01-01T00:00:00.0000000Z', '2020-01-01T00:00:00.0000000Z');
            INSERT INTO oauth_access_tokens (token, client_id, username, expires_at, created_at)
            VALUES ('stale-access', @client, 'u', '2020-01-01T00:00:00.0000000Z', '2020-01-01T00:00:00.0000000Z');
            """))
        {
            cmd.Parameters.AddWithValue("@client", client.ClientId);
            cmd.ExecuteNonQuery();
        }

        var removed = _store.CleanupExpired();
        Assert.True(removed >= 3, $"Expected at least 3 rows removed, got {removed}");

        Assert.Null(_store.ConsumeAuthCode("stale-code", null));
        Assert.Null(_store.ValidateAccessToken("stale-access"));
        Assert.Null(_store.RefreshTokens("stale-refresh"));

        // Live tokens still work
        Assert.NotNull(_store.ValidateAccessToken(live.AccessToken));
    }

    [Fact]
    public void Tokens_Are_URL_Safe_Base64()
    {
        var client = _store.RegisterClient("c", Array.Empty<string>());
        var pair = _store.IssueTokens(client.ClientId, "alice");

        Assert.DoesNotContain('+', pair.AccessToken);
        Assert.DoesNotContain('/', pair.AccessToken);
        Assert.DoesNotContain('=', pair.AccessToken);
        Assert.DoesNotContain('+', pair.RefreshToken);
    }

    private static string ComputeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}

public class PasswordHasherTests
{
    [Fact]
    public void Hash_And_Verify_RoundTrip()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
        Assert.False(PasswordHasher.Verify("wrong password", hash));
    }

    [Fact]
    public void Verify_Rejects_Malformed_Encoded()
    {
        Assert.False(PasswordHasher.Verify("anything", "not-a-valid-hash"));
        Assert.False(PasswordHasher.Verify("anything", ""));
        Assert.False(PasswordHasher.Verify("anything", "pbkdf2-sha256$abc"));
        Assert.False(PasswordHasher.Verify("anything", "pbkdf2-sha256$100000$bad-base64$bad"));
    }

    [Fact]
    public void Verify_Rejects_Wrong_Algorithm()
    {
        Assert.False(PasswordHasher.Verify("p", "argon2id$100000$AAAA$BBBB"));
    }

    [Fact]
    public void Different_Salts_Produce_Different_Hashes()
    {
        var hash1 = PasswordHasher.Hash("same password");
        var hash2 = PasswordHasher.Hash("same password");

        Assert.NotEqual(hash1, hash2);
        Assert.True(PasswordHasher.Verify("same password", hash1));
        Assert.True(PasswordHasher.Verify("same password", hash2));
    }

    [Fact]
    public void Hash_Throws_On_Empty_Password()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.Hash(""));
    }
}
