using DriveShield.Api.Application;

namespace DriveShield.Api.Auth;

// ------------------------------------------------------------ requests
public record OtpRequest(string? Identifier);
public record VerifyRequest(Guid ChallengeId, string? Code, string? DisplayName);
public record RefreshRequest(string? RefreshToken);
public record LinkVerifyRequest(Guid ChallengeId, string? Code);

// ------------------------------------------------------------ responses

/// <summary>What the person sees after asking for a code. DevCode is only ever filled in on a development machine.</summary>
public record OtpStarted(
    Guid ChallengeId,
    string Channel,
    string DestinationMasked,
    int ExpiresInSeconds,
    int ResendAfterSeconds,
    string? DevCode);

/// <summary>Signed in: a short-lived access token, a long-lived refresh token, and who the person is.</summary>
public record AuthResponse(string AccessToken, int ExpiresIn, string RefreshToken, MeDto User);
