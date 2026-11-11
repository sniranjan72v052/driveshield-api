using System.Security.Claims;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Auth;

/// <summary>
/// Maps the authenticated principal to our AppUser row, creating it on first sign-in.
/// Roles live in the database (not in the token) so an admin can change them without re-issuing tokens.
/// </summary>
public class UserResolver(AppDbContext db, AccountService accounts)
{
    public async Task<AppUser> ResolveAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var externalId = principal.FindFirstValue("oid")
                         ?? principal.FindFirstValue("sub")
                         ?? throw new UnauthorizedAccessException("The token has no subject claim.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, ct);
        if (user is not null)
        {
            // Admin rights follow Auth:AdminEmails on every request, so editing the list and restarting is enough.
            if (await accounts.SyncAdminAsync(user, ct)) await db.SaveChangesAsync(ct);
            return user;
        }

        user = new AppUser
        {
            ExternalId = externalId,
            DisplayName = principal.FindFirstValue("name") ?? ""
        };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first requests raced; the other one created the row.
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.FirstAsync(u => u.ExternalId == externalId, ct);
        }
        return user;
    }
}
