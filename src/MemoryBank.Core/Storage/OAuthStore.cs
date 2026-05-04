using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MemoryBank.Core.Storage;

public class OAuthStore
{
    public static readonly TimeSpan AuthCodeLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    private readonly MemoryBankDb _db;
    private readonly ILogger<OAuthStore> _logger;

    public OAuthStore(MemoryBankDb db, ILogger<OAuthStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public OAuthClient RegisterClient(string clientName, string[] redirectUris)
    {
        var clientId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        using var cmd = _db.CreateCommand("""
            INSERT INTO oauth_clients (client_id, client_name, redirect_uris, created_at)
            VALUES (@id, @name, @uris, @created)
            """);
        cmd.Parameters.AddWithValue("@id", clientId);
        cmd.Parameters.AddWithValue("@name", clientName);
        cmd.Parameters.AddWithValue("@uris", JsonSerializer.Serialize(redirectUris));
        cmd.Parameters.AddWithValue("@created", FormatTime(now));
        cmd.ExecuteNonQuery();
        _logger.LogInformation("OAuth client {ClientId} ({ClientName}) registered", clientId, clientName);
        return new OAuthClient(clientId, clientName, redirectUris, now);
    }

    public OAuthClient? GetClient(string clientId)
    {
        using var cmd = _db.CreateCommand("""
            SELECT client_id, client_name, redirect_uris, created_at FROM oauth_clients WHERE client_id = @id
            """);
        cmd.Parameters.AddWithValue("@id", clientId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var uris = JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? Array.Empty<string>();
        return new OAuthClient(reader.GetString(0), reader.GetString(1), uris, ParseTime(reader.GetString(3)));
    }

    public string IssueAuthCode(string clientId, string redirectUri, string? codeChallenge, string? codeChallengeMethod, string username)
    {
        var code = GenerateOpaqueToken();
        var now = DateTime.UtcNow;
        using var cmd = _db.CreateCommand("""
            INSERT INTO oauth_authorization_codes
                (code, client_id, redirect_uri, code_challenge, code_challenge_method, username, expires_at, created_at)
            VALUES (@code, @client, @uri, @challenge, @method, @user, @expires, @created)
            """);
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@client", clientId);
        cmd.Parameters.AddWithValue("@uri", redirectUri);
        cmd.Parameters.AddWithValue("@challenge", (object?)codeChallenge ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@method", (object?)codeChallengeMethod ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@user", username);
        cmd.Parameters.AddWithValue("@expires", FormatTime(now.Add(AuthCodeLifetime)));
        cmd.Parameters.AddWithValue("@created", FormatTime(now));
        cmd.ExecuteNonQuery();
        return code;
    }

    /// <summary>
    /// Single-use: deletes the row on lookup so it cannot be replayed.
    /// </summary>
    public OAuthAuthorizationCode? ConsumeAuthCode(string code, string? codeVerifier)
    {
        using var tx = _db.BeginTransaction();
        try
        {
            string clientId, redirectUri, username;
            string? challenge, method;
            DateTime expiresAt, createdAt;

            using (var sel = _db.CreateCommand("""
                SELECT client_id, redirect_uri, code_challenge, code_challenge_method, username, expires_at, created_at
                FROM oauth_authorization_codes WHERE code = @code
                """, tx))
            {
                sel.Parameters.AddWithValue("@code", code);
                using var reader = sel.ExecuteReader();
                if (!reader.Read())
                {
                    tx.Commit();
                    return null;
                }
                clientId    = reader.GetString(0);
                redirectUri = reader.GetString(1);
                challenge   = reader.IsDBNull(2) ? null : reader.GetString(2);
                method      = reader.IsDBNull(3) ? null : reader.GetString(3);
                username    = reader.GetString(4);
                expiresAt   = ParseTime(reader.GetString(5));
                createdAt   = ParseTime(reader.GetString(6));
            }

            using (var del = _db.CreateCommand("DELETE FROM oauth_authorization_codes WHERE code = @code", tx))
            {
                del.Parameters.AddWithValue("@code", code);
                del.ExecuteNonQuery();
            }

            tx.Commit();

            if (DateTime.UtcNow > expiresAt) return null;
            if (!string.IsNullOrEmpty(challenge) && !VerifyPkce(challenge, method, codeVerifier)) return null;

            return new OAuthAuthorizationCode(code, clientId, redirectUri, username, expiresAt, createdAt);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public OAuthTokenPair IssueTokens(string clientId, string username)
        => InsertTokenPair(clientId, username, transaction: null);

    public OAuthAccessToken? ValidateAccessToken(string token)
    {
        using var cmd = _db.CreateCommand("""
            SELECT token, client_id, refresh_token, username, expires_at, created_at
            FROM oauth_access_tokens WHERE token = @token
            """);
        cmd.Parameters.AddWithValue("@token", token);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var expiresAt = ParseTime(reader.GetString(4));
        if (DateTime.UtcNow > expiresAt) return null;
        return new OAuthAccessToken(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            expiresAt,
            ParseTime(reader.GetString(5)));
    }

    /// <summary>
    /// Rotates the refresh token: deletes the old one (cascading its access tokens away) and issues a fresh pair.
    /// </summary>
    public OAuthTokenPair? RefreshTokens(string refreshToken)
    {
        using var tx = _db.BeginTransaction();
        try
        {
            string clientId, username;
            DateTime expiresAt;
            using (var sel = _db.CreateCommand("""
                SELECT client_id, username, expires_at FROM oauth_refresh_tokens WHERE token = @token
                """, tx))
            {
                sel.Parameters.AddWithValue("@token", refreshToken);
                using var reader = sel.ExecuteReader();
                if (!reader.Read()) { tx.Commit(); return null; }
                clientId  = reader.GetString(0);
                username  = reader.GetString(1);
                expiresAt = ParseTime(reader.GetString(2));
            }
            if (DateTime.UtcNow > expiresAt) { tx.Commit(); return null; }

            using (var del = _db.CreateCommand("DELETE FROM oauth_refresh_tokens WHERE token = @token", tx))
            {
                del.Parameters.AddWithValue("@token", refreshToken);
                del.ExecuteNonQuery();
            }

            var pair = InsertTokenPair(clientId, username, tx);
            tx.Commit();
            return pair;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void RevokeRefreshToken(string refreshToken)
    {
        using var cmd = _db.CreateCommand("DELETE FROM oauth_refresh_tokens WHERE token = @token");
        cmd.Parameters.AddWithValue("@token", refreshToken);
        cmd.ExecuteNonQuery();
    }

    public int CleanupExpired()
    {
        var now = FormatTime(DateTime.UtcNow);
        var total = 0;
        using var tx = _db.BeginTransaction();
        try
        {
            using (var c1 = _db.CreateCommand("DELETE FROM oauth_authorization_codes WHERE expires_at < @now", tx))
            {
                c1.Parameters.AddWithValue("@now", now);
                total += c1.ExecuteNonQuery();
            }
            using (var c2 = _db.CreateCommand("DELETE FROM oauth_access_tokens WHERE expires_at < @now", tx))
            {
                c2.Parameters.AddWithValue("@now", now);
                total += c2.ExecuteNonQuery();
            }
            using (var c3 = _db.CreateCommand("DELETE FROM oauth_refresh_tokens WHERE expires_at < @now", tx))
            {
                c3.Parameters.AddWithValue("@now", now);
                total += c3.ExecuteNonQuery();
            }
            tx.Commit();
            return total;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private OAuthTokenPair InsertTokenPair(string clientId, string username, Microsoft.Data.Sqlite.SqliteTransaction? transaction)
    {
        var now = DateTime.UtcNow;
        var refreshToken = GenerateOpaqueToken();
        var accessToken = GenerateOpaqueToken();
        var refreshExpires = now.Add(RefreshTokenLifetime);
        var accessExpires = now.Add(AccessTokenLifetime);

        var ownsTransaction = transaction is null;
        var tx = transaction ?? _db.BeginTransaction();
        try
        {
            using (var refCmd = _db.CreateCommand("""
                INSERT INTO oauth_refresh_tokens (token, client_id, username, expires_at, created_at)
                VALUES (@token, @client, @user, @expires, @created)
                """, tx))
            {
                refCmd.Parameters.AddWithValue("@token", refreshToken);
                refCmd.Parameters.AddWithValue("@client", clientId);
                refCmd.Parameters.AddWithValue("@user", username);
                refCmd.Parameters.AddWithValue("@expires", FormatTime(refreshExpires));
                refCmd.Parameters.AddWithValue("@created", FormatTime(now));
                refCmd.ExecuteNonQuery();
            }

            using (var accCmd = _db.CreateCommand("""
                INSERT INTO oauth_access_tokens (token, client_id, refresh_token, username, expires_at, created_at)
                VALUES (@token, @client, @refresh, @user, @expires, @created)
                """, tx))
            {
                accCmd.Parameters.AddWithValue("@token", accessToken);
                accCmd.Parameters.AddWithValue("@client", clientId);
                accCmd.Parameters.AddWithValue("@refresh", refreshToken);
                accCmd.Parameters.AddWithValue("@user", username);
                accCmd.Parameters.AddWithValue("@expires", FormatTime(accessExpires));
                accCmd.Parameters.AddWithValue("@created", FormatTime(now));
                accCmd.ExecuteNonQuery();
            }

            if (ownsTransaction) tx.Commit();
            return new OAuthTokenPair(accessToken, accessExpires, refreshToken, refreshExpires);
        }
        catch
        {
            if (ownsTransaction) tx.Rollback();
            throw;
        }
        finally
        {
            if (ownsTransaction) tx.Dispose();
        }
    }

    private static string GenerateOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static bool VerifyPkce(string challenge, string? method, string? verifier)
    {
        if (string.IsNullOrEmpty(verifier)) return false;
        var resolvedMethod = string.IsNullOrEmpty(method) ? "S256" : method;

        if (resolvedMethod.Equals("S256", StringComparison.OrdinalIgnoreCase))
        {
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
            var actual = Convert.ToBase64String(hash).Replace("+", "-").Replace("/", "_").TrimEnd('=');
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actual),
                Encoding.ASCII.GetBytes(challenge));
        }
        if (resolvedMethod.Equals("plain", StringComparison.OrdinalIgnoreCase))
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(verifier),
                Encoding.ASCII.GetBytes(challenge));
        }
        return false;
    }

    private static string FormatTime(DateTime t) => t.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    private static DateTime ParseTime(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
}
