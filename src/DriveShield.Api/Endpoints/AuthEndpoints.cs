using System.Globalization;
using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;

namespace DriveShield.Api.Endpoints;

/// <summary>
/// Sign in with an email address or a mobile number, no password.
///
///   POST /api/auth/otp/start     { identifier }                          sends a 6-digit code  -> { challengeId, ... }
///   POST /api/auth/otp/verify    { challengeId, code, displayName? }     checks it             -> { accessToken, refreshToken, user }
///   POST /api/auth/refresh       { refreshToken }                        new pair of tokens
///   POST /api/auth/logout        { refreshToken }                        ends that sign-in
///   POST /api/auth/link/start    { identifier }     (signed in)          code to add a second email or number
///   POST /api/auth/link/verify   { challengeId, code }   (signed in)     adds it to the account -> user
///
/// Customers and providers use the same sign-in. A new email or number creates a customer account; a customer
/// becomes a provider by registering. Every route here is limited to 30 requests a minute per address.
/// </summary>
public static class AuthEndpoints
{
    private const string WrongCode = "That code is wrong or has expired. Check it, or ask for a new one.";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        g.MapPost("/otp/start", StartSignIn);
        g.MapPost("/otp/verify", VerifySignIn);
        g.MapPost("/refresh", Refresh);
        g.MapPost("/logout", Logout);

        g.MapPost("/link/start", StartLink).RequireAuthorization();
        g.MapPost("/link/verify", VerifyLink).RequireAuthorization();
    }

    private static IResult FromStart(OtpStartResult result, HttpContext http)
    {
        if (result.Started is not null) return Results.Ok(result.Started);

        if (result.RetryAfterSeconds > 0)
            http.Response.Headers.RetryAfter = result.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Results.Json(new { error = result.Error, retryAfterSeconds = result.RetryAfterSeconds }, statusCode: result.StatusCode);
    }

    private static async Task<IResult> StartSignIn(OtpRequest req, HttpContext http, OtpService otp, CancellationToken ct) =>
        FromStart(await otp.StartAsync(req.Identifier, OtpPurpose.SignIn, null, ct), http);

    private static async Task<IResult> VerifySignIn(
        VerifyRequest req, OtpService otp, AccountService accounts, TokenService tokens, AppDbContext db, CancellationToken ct)
    {
        var verified = await otp.VerifyAsync(req.ChallengeId, req.Code, OtpPurpose.SignIn, null, ct);
        if (verified is null) return Results.BadRequest(new { error = WrongCode });

        var user = await accounts.SignInAsync(verified.Channel, verified.Destination, req.DisplayName, ct);
        var pair = await tokens.IssueAsync(user, ct);
        return Results.Ok(new AuthResponse(
            pair.AccessToken, pair.ExpiresInSeconds, pair.RefreshToken, await MeEndpoints.BuildMeAsync(db, user, ct)));
    }

    private static async Task<IResult> Refresh(RefreshRequest req, TokenService tokens, AccountService accounts, AppDbContext db, CancellationToken ct)
    {
        var result = await tokens.RefreshAsync(req.RefreshToken, ct);
        if (result is null) return Results.Json(new { error = "Please sign in again." }, statusCode: StatusCodes.Status401Unauthorized);
        if (await accounts.SyncAdminAsync(result.User, ct)) await db.SaveChangesAsync(ct);

        return Results.Ok(new AuthResponse(
            result.Pair.AccessToken, result.Pair.ExpiresInSeconds, result.Pair.RefreshToken,
            await MeEndpoints.BuildMeAsync(db, result.User, ct)));
    }

    private static async Task<IResult> Logout(RefreshRequest req, TokenService tokens, CancellationToken ct)
    {
        await tokens.RevokeAsync(req.RefreshToken, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> StartLink(OtpRequest req, HttpContext http, UserResolver users, OtpService otp, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        return FromStart(await otp.StartAsync(req.Identifier, OtpPurpose.Link, user.Id, ct), http);
    }

    private static async Task<IResult> VerifyLink(
        LinkVerifyRequest req, HttpContext http, UserResolver users, OtpService otp, AccountService accounts,
        AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var verified = await otp.VerifyAsync(req.ChallengeId, req.Code, OtpPurpose.Link, user.Id, ct);
        if (verified is null) return Results.BadRequest(new { error = WrongCode });

        var (linked, error) = await accounts.LinkAsync(user.Id, verified.Channel, verified.Destination, ct);
        if (linked is null) return Results.Conflict(new { error });
        return Results.Ok(await MeEndpoints.BuildMeAsync(db, linked, ct));
    }
}
