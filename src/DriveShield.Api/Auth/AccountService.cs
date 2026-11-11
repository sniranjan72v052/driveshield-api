using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DriveShield.Api.Auth;

/// <summary>Finds or creates the account behind a verified email or number, and keeps admin rights in step with Auth:AdminEmails.</summary>
public class AccountService(AppDbContext db, IOptions<AccountOptions> options, ILogger<AccountService> log)
{
    public static string CleanName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var clean = new string(raw.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return clean.Length > 80 ? clean[..80] : clean;
    }

    private Task<AppUser?> FindAsync(OtpChannel channel, string destination, CancellationToken ct) =>
        channel == OtpChannel.Email
            ? db.Users.FirstOrDefaultAsync(u => u.Email == destination, ct)
            : db.Users.FirstOrDefaultAsync(u => u.LoginPhone == destination, ct);

    /// <summary>Called after a sign-in code is verified. Returning people are found; new people get an account on the spot.</summary>
    public async Task<AppUser> SignInAsync(OtpChannel channel, string destination, string? displayName, CancellationToken ct)
    {
        var user = await FindAsync(channel, destination, ct);

        if (user is null)
        {
            user = new AppUser
            {
                ExternalId = "otp-" + Guid.NewGuid().ToString("N"),
                DisplayName = CleanName(displayName)
            };
            if (channel == OtpChannel.Email) user.Email = destination;
            else user.LoginPhone = destination;

            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two first sign-ins raced and the other one created the account: use that one.
                db.Entry(user).State = EntityState.Detached;
                var existing = await FindAsync(channel, destination, ct);
                if (existing is null) throw;
                user = existing;
            }
        }
        else if (string.IsNullOrWhiteSpace(user.DisplayName) && CleanName(displayName) is { Length: > 0 } name)
        {
            user.DisplayName = name;
        }

        await ApplyAdminRuleAsync(user, ct);
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>Adds a verified second email or number to a signed-in account.</summary>
    public async Task<(AppUser? User, string? Error)> LinkAsync(Guid userId, OtpChannel channel, string destination, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return (null, "Please sign in again.");

        if (channel == OtpChannel.Email)
        {
            if (user.Email is not null) return (null, "This account already has an email address.");
            user.Email = destination;
        }
        else
        {
            if (user.LoginPhone is not null) return (null, "This account already has a mobile number.");
            user.LoginPhone = destination;
        }

        await ApplyAdminRuleAsync(user, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return (null, "That email or number is already used by another account.");
        }
        return (user, null);
    }

    /// <summary>
    /// Auth:AdminEmails is the source of truth for accounts made by code sign-in: being on the list makes an admin,
    /// being taken off the list ends it. Accounts without an email, and the local dev-admin, are left alone.
    /// </summary>
    private Task ApplyAdminRuleAsync(AppUser user, CancellationToken ct) => SyncAdminAsync(user, ct);

    /// <summary>
    /// Brings the account's role in line with Auth:AdminEmails. Changes the entity only; the caller saves.
    /// Returns true when the role changed. Runs on every request, so a change to the list takes effect
    /// without signing out.
    /// </summary>
    public async Task<bool> SyncAdminAsync(AppUser user, CancellationToken ct)
    {
        if (user.Email is null) return false;

        var admins = options.Value.AdminEmails
            .Select(e => e.Trim().ToLowerInvariant())
            .Where(e => e.Length > 0)
            .ToHashSet();
        var shouldBeAdmin = admins.Contains(user.Email);

        if (shouldBeAdmin && user.Role != UserRole.Admin)
        {
            user.Role = UserRole.Admin;
            log.LogInformation("{Email} is on Auth:AdminEmails, so this account is now an admin.", user.Email);
            return true;
        }
        else if (!shouldBeAdmin && user.Role == UserRole.Admin && user.ExternalId.StartsWith("otp-", StringComparison.Ordinal))
        {
            var isProvider = await db.ProviderProfiles.AnyAsync(p => p.UserId == user.Id, ct);
            user.Role = isProvider ? UserRole.Provider : UserRole.Customer;
            log.LogInformation("{Email} is no longer on Auth:AdminEmails, so this account is no longer an admin.", user.Email);
            return true;
        }
        return false;
    }
}
