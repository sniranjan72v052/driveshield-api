using DriveShield.Api.Application;
using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Endpoints;

public static class MeEndpoints
{
    /// <summary>The signed-in person as the apps see them: used after sign-in to decide which screens to show.</summary>
    public static async Task<MeDto> BuildMeAsync(AppDbContext db, AppUser user, CancellationToken ct)
    {
        var providerId = await db.ProviderProfiles
            .Where(p => p.UserId == user.Id)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        var vehicles = await db.Vehicles.CountAsync(v => v.OwnerId == user.Id, ct);
        return new MeDto(user.Id, user.DisplayName, user.Role.ToString(), providerId, user.Email, user.LoginPhone, vehicles);
    }

    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me", async (HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct) =>
        {
            var user = await users.ResolveAsync(http.User, ct);
            return Results.Ok(await BuildMeAsync(db, user, ct));
        }).RequireAuthorization();

        // Lets a person set or change the name shown in the app.
        app.MapPut("/api/me", async (UpdateMeRequest req, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct) =>
        {
            var name = AccountService.CleanName(req.DisplayName);
            if (name.Length == 0) return Results.BadRequest(new { error = "Enter your name." });

            var user = await users.ResolveAsync(http.User, ct);
            user.DisplayName = name;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await BuildMeAsync(db, user, ct));
        }).RequireAuthorization();
    }
}
