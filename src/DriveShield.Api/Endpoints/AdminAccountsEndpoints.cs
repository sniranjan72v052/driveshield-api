using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DriveShield.Api.Endpoints;

/// <summary>
/// Who may run the service. Only a super admin can use these routes.
///
///   GET    /api/admin/admins            everyone who is an admin or super admin, and where that comes from
///   POST   /api/admin/admins            { email }  makes that email an admin (works before they have ever signed in)
///   DELETE /api/admin/admins/{userId}   takes admin rights away
///
/// Super admins and anyone listed in Auth:AdminEmails are managed in the API settings, not here.
/// </summary>
public static class AdminAccountsEndpoints
{
    public record AdminRow(Guid Id, string? Email, string? LoginPhone, string DisplayName, string Role, string Source, bool Removable);
    public record AddAdminRequest(string? Email);

    public static void Map(RouteGroupBuilder g)
    {
        g.MapGet("/admins", List);
        g.MapPost("/admins", Add);
        g.MapDelete("/admins/{userId:guid}", Remove);
    }

    private static async Task<AppUser?> SuperAsync(HttpContext http, UserResolver users, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        return user.Role == UserRole.SuperAdmin ? user : null;
    }

    private static HashSet<string> Emails(IEnumerable<string> list) =>
        list.Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 0).ToHashSet();

    private static AdminRow ToRow(AppUser u, HashSet<string> supers, HashSet<string> admins)
    {
        var email = u.Email ?? "";
        var source = u.Role == UserRole.SuperAdmin || supers.Contains(email) ? "Settings (super admin)"
            : admins.Contains(email) ? "Settings (Auth:AdminEmails)"
            : u.AdminGranted ? "Added in the app"
            : "Local account";
        var removable = u.Role == UserRole.Admin && u.AdminGranted && !admins.Contains(email) && !supers.Contains(email);
        return new AdminRow(u.Id, u.Email, u.LoginPhone, u.DisplayName, u.Role.ToString(), source, removable);
    }

    private static async Task<IResult> List(
        HttpContext http, UserResolver users, AppDbContext db, IOptions<AccountOptions> options, CancellationToken ct)
    {
        if (await SuperAsync(http, users, ct) is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var supers = Emails(options.Value.SuperAdminEmails);
        var admins = Emails(options.Value.AdminEmails);
        var rows = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Admin || u.Role == UserRole.SuperAdmin)
            .OrderBy(u => u.Role == UserRole.SuperAdmin ? 0 : 1).ThenBy(u => u.Email)
            .ToListAsync(ct);
        return Results.Ok(rows.Select(u => ToRow(u, supers, admins)));
    }

    private static async Task<IResult> Add(
        AddAdminRequest req, HttpContext http, UserResolver users, AppDbContext db, IOptions<AccountOptions> options, CancellationToken ct)
    {
        if (await SuperAsync(http, users, ct) is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var parsed = Identifiers.Parse(req.Email, options.Value.DefaultCountryCode);
        if (parsed is not { Channel: OtpChannel.Email })
            return Results.BadRequest(new { error = "Enter a valid email address." });
        var email = parsed.Value.Value;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            // Not signed in yet: create the account now. It is found by this email when they sign in with a code.
            user = new AppUser { ExternalId = "otp-" + Guid.NewGuid().ToString("N"), Email = email, DisplayName = "" };
            db.Users.Add(user);
        }

        user.AdminGranted = true;
        if (user.Role is UserRole.Customer or UserRole.Provider) user.Role = UserRole.Admin;

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "That email could not be added. Try again." }); }

        return Results.Ok(ToRow(user, Emails(options.Value.SuperAdminEmails), Emails(options.Value.AdminEmails)));
    }

    private static async Task<IResult> Remove(
        Guid userId, HttpContext http, UserResolver users, AppDbContext db, IOptions<AccountOptions> options, CancellationToken ct)
    {
        var me = await SuperAsync(http, users, ct);
        if (me is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (me.Id == userId) return Results.BadRequest(new { error = "You cannot remove your own access." });

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return Results.NotFound();

        var supers = Emails(options.Value.SuperAdminEmails);
        var admins = Emails(options.Value.AdminEmails);
        var email = user.Email ?? "";
        if (user.Role == UserRole.SuperAdmin || supers.Contains(email))
            return Results.Conflict(new { error = "Super admins are set in the API settings (Auth:SuperAdminEmails)." });
        if (admins.Contains(email))
            return Results.Conflict(new { error = "This admin is set in the API settings (Auth:AdminEmails). Remove the email there." });

        user.AdminGranted = false;
        if (user.Role == UserRole.Admin)
        {
            var isProvider = await db.ProviderProfiles.AnyAsync(p => p.UserId == user.Id, ct);
            user.Role = isProvider ? UserRole.Provider : UserRole.Customer;
        }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
