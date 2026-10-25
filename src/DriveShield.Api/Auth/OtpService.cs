using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DriveShield.Api.Auth;

public record OtpStartResult(OtpStarted? Started, int StatusCode, string? Error, int RetryAfterSeconds)
{
    public static OtpStartResult Ok(OtpStarted started) => new(started, 200, null, 0);
    public static OtpStartResult Fail(int statusCode, string error, int retryAfterSeconds = 0) => new(null, statusCode, error, retryAfterSeconds);
}

public record OtpVerified(OtpChannel Channel, string Destination);

/// <summary>
/// Sends and checks one-time sign-in codes.
///  - A code is 6 digits, lives 10 minutes, and is stored only as an HMAC hash.
///  - Five wrong guesses kill the code. Five codes per email/number per hour, one every 30 seconds.
///  - Asking for a code never reveals whether an account exists: new and returning people get the same answer.
/// </summary>
public class OtpService(
    AppDbContext db,
    IOtpSender sender,
    IOptions<OtpOptions> otpOptions,
    IOptions<AccountOptions> accountOptions,
    IOptions<JwtOptions> jwtOptions,
    IWebHostEnvironment env,
    ILogger<OtpService> log)
{
    public async Task<OtpStartResult> StartAsync(string? identifier, OtpPurpose purpose, Guid? userId, CancellationToken ct)
    {
        var parsed = Identifiers.Parse(identifier, accountOptions.Value.DefaultCountryCode);
        if (parsed is not { } id)
            return OtpStartResult.Fail(400, "Enter a valid email address, or a mobile number with its country code.");

        var o = otpOptions.Value;
        var now = DateTimeOffset.UtcNow;

        if (purpose == OtpPurpose.Link)
        {
            var me = userId is { } uid ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct) : null;
            if (me is null) return OtpStartResult.Fail(401, "Please sign in again.");

            if (id.Channel == OtpChannel.Email && me.Email is not null)
                return OtpStartResult.Fail(409, "This account already has an email address.");
            if (id.Channel == OtpChannel.Phone && me.LoginPhone is not null)
                return OtpStartResult.Fail(409, "This account already has a mobile number.");

            var taken = id.Channel == OtpChannel.Email
                ? await db.Users.AnyAsync(u => u.Email == id.Value, ct)
                : await db.Users.AnyAsync(u => u.LoginPhone == id.Value, ct);
            if (taken) return OtpStartResult.Fail(409, "That email or number is already used by another account.");
        }

        // Newest first. The list is at most a handful of rows: the hourly cap is small.
        var since = now.AddHours(-1);
        var recent = await db.OtpChallenges.AsNoTracking()
            .Where(c => c.Destination == id.Value && c.CreatedAt > since)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);

        if (recent.Count >= o.MaxSendsPerHour)
        {
            var wait = Math.Max(1, (int)Math.Ceiling((recent[^1].AddHours(1) - now).TotalSeconds));
            var minutes = (wait + 59) / 60;
            return OtpStartResult.Fail(429, $"Too many codes were sent to this {(id.Channel == OtpChannel.Email ? "email" : "number")}. Try again in {minutes} {(minutes == 1 ? "minute" : "minutes")}.", wait);
        }

        if (recent.Count > 0)
        {
            var left = o.ResendSeconds - (int)Math.Floor((now - recent[0]).TotalSeconds);
            if (left > 0) return OtpStartResult.Fail(429, $"Please wait {left} seconds before asking for another code.", left);
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var challenge = new OtpChallenge
        {
            Channel = id.Channel,
            Destination = id.Value,
            Purpose = purpose,
            UserId = userId,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(o.CodeLifetimeMinutes)
        };
        challenge.CodeHash = HashCode(challenge.Id, code);
        db.OtpChallenges.Add(challenge);
        await db.SaveChangesAsync(ct);

        try
        {
            await sender.SendAsync(id.Channel, id.Value, code, o.CodeLifetimeMinutes, ct);
        }
        catch (DeliveryNotConfiguredException ex)
        {
            await DropAsync(challenge.Id);
            log.LogError("{Message}", ex.Message);
            return OtpStartResult.Fail(503, id.Channel == OtpChannel.Email
                ? "Signing in with email isn't available right now. Try your mobile number, or come back later."
                : "Signing in with a mobile number isn't available right now. Try your email, or come back later.");
        }
        catch (DeliveryFailedException ex)
        {
            await DropAsync(challenge.Id); // a failed send should not use up the person's hourly allowance
            log.LogError(ex, "Could not deliver a sign-in code by {Channel}", id.Channel);
            return OtpStartResult.Fail(502, "We couldn't send the code. Please try again in a moment.");
        }

        var devCode = env.IsDevelopment() && o.ExposeDevCode ? code : null;
        return OtpStartResult.Ok(new OtpStarted(
            challenge.Id, id.Channel.ToString(), Identifiers.Mask(id.Channel, id.Value),
            o.CodeLifetimeMinutes * 60, o.ResendSeconds, devCode));
    }

    /// <summary>Checks a code. Returns null for every kind of failure, so a wrong, expired, used or unknown code look alike.</summary>
    public async Task<OtpVerified?> VerifyAsync(Guid challengeId, string? code, OtpPurpose purpose, Guid? userId, CancellationToken ct)
    {
        var digits = new string((code ?? "").Where(c => c is >= '0' and <= '9').ToArray()); // "123 456" is fine
        if (digits.Length != 6) return null;

        var o = otpOptions.Value;
        var now = DateTimeOffset.UtcNow;

        var stored = await db.OtpChallenges.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == challengeId && c.Purpose == purpose, ct);
        if (stored is null || stored.ConsumedAt is not null || stored.ExpiresAt <= now || stored.Attempts >= o.MaxAttempts) return null;
        if (purpose == OtpPurpose.Link && (userId is null || stored.UserId != userId)) return null;

        // Count the guess first, in one atomic step, so parallel guesses cannot beat the limit.
        var maxAttempts = o.MaxAttempts;
        var counted = await db.OtpChallenges
            .Where(c => c.Id == challengeId && c.ConsumedAt == null && c.Attempts < maxAttempts)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Attempts, c => c.Attempts + 1), ct);
        if (counted == 0) return null;

        var expected = Encoding.ASCII.GetBytes(stored.CodeHash);
        var actual = Encoding.ASCII.GetBytes(HashCode(stored.Id, digits));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;

        // Use it up, atomically: if two requests carry the right code at once, only one wins.
        var consumedAt = (DateTimeOffset?)now;
        var consumed = await db.OtpChallenges
            .Where(c => c.Id == challengeId && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, consumedAt), ct);
        return consumed == 1 ? new OtpVerified(stored.Channel, stored.Destination) : null;
    }

    private Task<int> DropAsync(Guid challengeId) =>
        db.OtpChallenges.Where(c => c.Id == challengeId).ExecuteDeleteAsync(CancellationToken.None);

    /// <summary>Keyed hash (HMAC-SHA256), so a database leak does not reveal live codes.</summary>
    private string HashCode(Guid challengeId, string code)
    {
        var key = Encoding.UTF8.GetBytes(jwtOptions.Value.SigningKey);
        var data = Encoding.UTF8.GetBytes($"otp:{challengeId:N}:{code}");
        return Convert.ToHexString(HMACSHA256.HashData(key, data));
    }
}
