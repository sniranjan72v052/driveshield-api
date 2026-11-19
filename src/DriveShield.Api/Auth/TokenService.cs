using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DriveShield.Api.Auth;

public record TokenPair(string AccessToken, int ExpiresInSeconds, string RefreshToken);
public record RefreshResult(TokenPair Pair, AppUser User);

/// <summary>
/// Access tokens: signed JWTs, 15 minutes. The "sub" claim is the account's ExternalId, which UserResolver already understands.
/// Refresh tokens: random, stored only as a SHA-256 hash, replaced on every use. If a token that was already used
/// shows up again long after, someone copied it, so every session of that person is ended.
/// </summary>
public class TokenService(AppDbContext db, IOptions<JwtOptions> options)
{
    private readonly JwtOptions _jwt = options.Value;

    public async Task<TokenPair> IssueAsync(AppUser user, CancellationToken ct)
    {
        var (token, row) = NewRefreshToken(user.Id);
        db.RefreshTokens.Add(row);
        await db.SaveChangesAsync(ct);
        return new TokenPair(CreateAccessToken(user), _jwt.AccessTokenMinutes * 60, token);
    }

    /// <summary>Swaps a refresh token for a fresh pair. Null means "sign in again".</summary>
    public async Task<RefreshResult?> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 200) return null;

        var hash = Hash(refreshToken);
        var row = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null) return null;

        var now = DateTimeOffset.UtcNow;

        if (row.RevokedAt is { } revokedAt)
        {
            // Used before. Within a few seconds it is most likely two browser tabs refreshing at once.
            // Later than that, a token that was swapped for a newer one (ReplacedById is set) has been copied,
            // so end every session of this person. A token that was only signed out (no replacement) just fails.
            if (row.ReplacedById is not null && now - revokedAt > TimeSpan.FromSeconds(30))
            {
                var userId = row.UserId;
                var revokedNow = (DateTimeOffset?)now;
                await db.RefreshTokens
                    .Where(t => t.UserId == userId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedNow), ct);
            }
            return null;
        }

        if (row.ExpiresAt <= now) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
        if (user is null) return null;

        var (token, next) = NewRefreshToken(user.Id);
        db.RefreshTokens.Add(next);
        await db.SaveChangesAsync(ct);

        // Retire the old token atomically. If another request got there first, drop the new one and fail.
        var oldId = row.Id;
        var retiredAt = (DateTimeOffset?)now;
        var replacedBy = (Guid?)next.Id;
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == oldId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, retiredAt)
                .SetProperty(t => t.ReplacedById, replacedBy), ct);
        if (claimed == 0)
        {
            var nextId = next.Id;
            await db.RefreshTokens.Where(t => t.Id == nextId).ExecuteDeleteAsync(CancellationToken.None);
            return null;
        }

        return new RefreshResult(new TokenPair(CreateAccessToken(user), _jwt.AccessTokenMinutes * 60, token), user);
    }

    /// <summary>Sign out: the refresh token stops working. Safe to call with an unknown or missing token.</summary>
    public async Task RevokeAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 200) return;
        var hash = Hash(refreshToken);
        var revokedAt = (DateTimeOffset?)DateTimeOffset.UtcNow;
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, revokedAt), ct);
    }

    public string CreateAccessToken(AppUser user)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Subject = new ClaimsIdentity(new List<Claim>
            {
                new("sub", user.ExternalId),
                new("name", user.DisplayName ?? "")
            }),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(_jwt.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private (string Token, RefreshToken Row) NewRefreshToken(Guid userId)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var row = new RefreshToken
        {
            UserId = userId,
            TokenHash = Hash(token),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays)
        };
        return (token, row);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
