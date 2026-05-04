namespace MemoryBank.Core.Storage;

public record OAuthClient(
    string ClientId,
    string ClientName,
    string[] RedirectUris,
    DateTime CreatedAt);

public record OAuthAuthorizationCode(
    string Code,
    string ClientId,
    string RedirectUri,
    string Username,
    DateTime ExpiresAt,
    DateTime CreatedAt);

public record OAuthAccessToken(
    string Token,
    string ClientId,
    string? RefreshToken,
    string Username,
    DateTime ExpiresAt,
    DateTime CreatedAt);

public record OAuthTokenPair(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt);
