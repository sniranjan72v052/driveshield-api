using DriveShield.Api.Application;
using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Endpoints;

public static class ProviderEndpoints
{
    public static void MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/providers").RequireAuthorization();

        g.MapPost("/register", Register);
        g.MapGet("/me", MyProfile);
        g.MapGet("/me/jobs", MyJobs);
        g.MapPut("/me/location", UpdateLocation);
        g.MapPut("/me/availability", UpdateAvailability);
        g.MapGet("/me/offers", MyOffers);
        g.MapGet("/nearby", Nearby);
    }

    private static async Task<IResult> Register(
        RegisterProviderRequest req, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.BusinessName) || req.BusinessName.Length > 200)
            return Results.BadRequest(new { error = "Business name is required (max 200 characters)." });
        if (req.Categories is not { Count: > 0 })
            return Results.BadRequest(new { error = "Choose at least one service." });
        if (!Geo.IsValid(req.Latitude, req.Longitude))
            return Results.BadRequest(new { error = "Latitude or longitude is out of range." });

        var user = await users.ResolveAsync(http.User, ct);
        if (await db.ProviderProfiles.AnyAsync(p => p.UserId == user.Id, ct))
            return Results.Conflict(new { error = "You are already registered as a provider." });

        var profile = new ProviderProfile
        {
            UserId = user.Id,
            BusinessName = req.BusinessName.Trim(),
            Description = req.Description?.Trim(),
            LicenseNumber = req.LicenseNumber?.Trim(),
            Location = Geo.Point(req.Latitude, req.Longitude),
            ServiceRadiusMeters = Math.Clamp(req.ServiceRadiusMeters ?? 15_000, 1_000, 100_000),
            ApprovalStatus = ProviderApprovalStatus.Pending, // an admin must approve before offers are sent
            IsAvailable = false,
            Services = req.Categories.Distinct().Select(c => new ProviderService { Category = c }).ToList()
        };
        db.ProviderProfiles.Add(profile);

        // The number customers call. If none is typed, use the mobile number they signed in with.
        if (!string.IsNullOrWhiteSpace(req.Phone)) user.Phone = req.Phone.Trim();
        else if (string.IsNullOrWhiteSpace(user.Phone) && user.LoginPhone is not null) user.Phone = user.LoginPhone;
        if (user.Role == UserRole.Customer) user.Role = UserRole.Provider;

        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/providers/{profile.Id}", new { id = profile.Id, approvalStatus = profile.ApprovalStatus.ToString() });
    }

    /// <summary>The signed-in provider's own profile (approval status, availability, services).</summary>
    private static async Task<IResult> MyProfile(HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var p = await db.ProviderProfiles.AsNoTracking()
            .Include(x => x.Services)
            .FirstOrDefaultAsync(x => x.UserId == user.Id, ct);
        if (p is null) return Results.NotFound();

        return Results.Ok(new ProviderProfileDto(
            p.Id, p.BusinessName, p.ApprovalStatus.ToString(), p.IsAvailable, p.ServiceRadiusMeters,
            p.Services.Select(s => s.Category).ToList(), p.AverageRating, p.ReviewCount,
            p.Location?.Y, p.Location?.X));
    }

    /// <summary>Cases this provider has accepted, most recently updated first.</summary>
    private static async Task<IResult> MyJobs(HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var providerId = await db.ProviderProfiles
            .Where(p => p.UserId == user.Id)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (providerId is null) return Results.NotFound();

        var rows = await db.CaseProviders.AsNoTracking()
            .Include(o => o.Case)
            .Where(o => o.ProviderId == providerId && o.OfferStatus == OfferStatus.Accepted)
            .OrderByDescending(o => o.Case.UpdatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Results.Ok(rows.Select(o => new JobDto(
            o.CaseId, o.Case.Status.ToString(), o.Case.IncidentType.ToString(),
            o.Category, o.Case.Description, o.Case.CreatedAt)));
    }

    private static async Task<IResult> UpdateLocation(
        LocationRequest req, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (!Geo.IsValid(req.Latitude, req.Longitude))
            return Results.BadRequest(new { error = "Latitude or longitude is out of range." });

        var user = await users.ResolveAsync(http.User, ct);
        var profile = await db.ProviderProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (profile is null) return Results.NotFound();

        profile.Location = Geo.Point(req.Latitude, req.Longitude);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateAvailability(
        AvailabilityRequest req, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var profile = await db.ProviderProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (profile is null) return Results.NotFound();
        if (profile.ApprovalStatus != ProviderApprovalStatus.Approved)
            return Results.Conflict(new { error = "Your registration has not been approved yet." });

        profile.IsAvailable = req.IsAvailable;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Open offers for the signed-in provider. Fallback for when SignalR/push is not connected.</summary>
    private static async Task<IResult> MyOffers(HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var providerId = await db.ProviderProfiles
            .Where(p => p.UserId == user.Id)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (providerId is null) return Results.NotFound();

        var now = DateTimeOffset.UtcNow;
        var offers = await db.CaseProviders.AsNoTracking()
            .Include(o => o.Case)
            .Where(o => o.ProviderId == providerId && o.OfferStatus == OfferStatus.Offered && o.ExpiresAt > now)
            .OrderBy(o => o.ExpiresAt)
            .ToListAsync(ct);

        // Before accepting, a provider only sees an approximate location (rounded to ~100 m).
        return Results.Ok(offers.Select(o => new OfferDto(
            o.Id, o.CaseId, o.Category, o.Case.IncidentType.ToString(), o.Case.Description,
            Math.Round(o.Case.Location.Y, 3), Math.Round(o.Case.Location.X, 3),
            o.DistanceMeters, o.ExpiresAt)));
    }

    /// <summary>Browse nearby approved, available providers (e.g. a "who is near me" map).</summary>
    private static async Task<IResult> Nearby(
        double lat, double lng, ServiceCategory? category, int? radiusMeters, int? limit,
        AppDbContext db, CancellationToken ct)
    {
        if (!Geo.IsValid(lat, lng))
            return Results.BadRequest(new { error = "Latitude or longitude is out of range." });

        var point = Geo.Point(lat, lng);
        var radius = Math.Clamp(radiusMeters ?? 10_000, 500, 100_000);
        var take = Math.Clamp(limit ?? 20, 1, 50);

        var q = db.ProviderProfiles.AsNoTracking()
            .Where(p => p.ApprovalStatus == ProviderApprovalStatus.Approved
                     && p.IsAvailable
                     && p.Location != null
                     && p.Location!.IsWithinDistance(point, radius));

        if (category is { } cat)
            q = q.Where(p => p.Services.Any(s => s.Category == cat));

        var rows = await q
            .OrderBy(p => p.Location!.Distance(point))
            .Take(take)
            .Select(p => new
            {
                p.Id,
                p.BusinessName,
                p.AverageRating,
                p.ReviewCount,
                Distance = p.Location!.Distance(point),
                Categories = p.Services.Select(s => s.Category).ToList()
            })
            .ToListAsync(ct);

        // Exact provider coordinates are intentionally not returned, only the distance.
        return Results.Ok(rows.Select(r => new NearbyProviderDto(
            r.Id, r.BusinessName, Math.Round(r.Distance), r.AverageRating, r.ReviewCount, r.Categories)));
    }
}
