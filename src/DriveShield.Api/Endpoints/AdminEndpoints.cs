using DriveShield.Api.Application;
using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin").RequireAuthorization();

        g.MapGet("/providers/pending", PendingProviders);
        g.MapPost("/providers/{providerId:guid}/approve", (Guid providerId, HttpContext h, UserResolver u, AppDbContext db, CancellationToken ct)
            => SetApproval(providerId, ProviderApprovalStatus.Approved, h, u, db, ct));
        g.MapPost("/providers/{providerId:guid}/reject", (Guid providerId, HttpContext h, UserResolver u, AppDbContext db, CancellationToken ct)
            => SetApproval(providerId, ProviderApprovalStatus.Rejected, h, u, db, ct));
        g.MapPost("/cases/{caseId:guid}/rematch", Rematch);
        g.MapGet("/photos", RecentPhotos);
        AdminDirectoryEndpoints.Map(g);
    }

    private static async Task<bool> IsAdminAsync(HttpContext http, UserResolver users, CancellationToken ct) =>
        (await users.ResolveAsync(http.User, ct)).Role == UserRole.Admin;

    private static async Task<IResult> PendingProviders(HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (!await IsAdminAsync(http, users, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var rows = await db.ProviderProfiles.AsNoTracking()
            .Where(p => p.ApprovalStatus == ProviderApprovalStatus.Pending)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id,
                p.BusinessName,
                p.LicenseNumber,
                p.CreatedAt,
                Phone = p.User.Phone,
                Categories = p.Services.Select(s => s.Category).ToList()
            })
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> SetApproval(
        Guid providerId, ProviderApprovalStatus status, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (!await IsAdminAsync(http, users, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var profile = await db.ProviderProfiles.FirstOrDefaultAsync(p => p.Id == providerId, ct);
        if (profile is null) return Results.NotFound();

        profile.ApprovalStatus = status;
        if (status != ProviderApprovalStatus.Approved) profile.IsAvailable = false;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id = profile.Id, approvalStatus = profile.ApprovalStatus.ToString() });
    }

    /// <summary>
    /// The photo review queue: every photo still on a case, newest first, so an admin can look through
    /// recent uploads and remove anything inappropriate (DELETE /api/cases/{id}/photos/{photoId}?reason=...).
    /// Page backwards with the nextBefore value from the previous page.
    /// </summary>
    private static async Task<IResult> RecentPhotos(
        DateTimeOffset? before, int? take, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (!await IsAdminAsync(http, users, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var limit = Math.Clamp(take ?? 24, 1, 60);

        var query = db.CasePhotos.AsNoTracking().Where(p => p.RemovedAt == null);
        if (before is { } cutoff) query = query.Where(p => p.CreatedAt < cutoff);

        var rows = await (
            from p in query
            join c in db.CaseComments on p.CommentId equals c.Id
            join k in db.Cases on p.CaseId equals k.Id
            orderby p.CreatedAt descending
            select new { p.Id, p.CaseId, p.CreatedAt, c.Step, c.Actor, k.IncidentType })
            .Take(limit + 1)
            .ToListAsync(ct);

        var page = rows.Take(limit).ToList();
        var items = page
            .Select(r => new AdminPhotoDto(r.Id, r.CaseId, r.IncidentType.ToString(), r.Step.ToString(), r.Actor.ToString(), r.CreatedAt))
            .ToList();
        DateTimeOffset? nextBefore = rows.Count > limit ? page[^1].CreatedAt : null;

        return Results.Ok(new { items, nextBefore });
    }

    /// <summary>Re-run matching for an escalated case, optionally with a wider search radius.</summary>
    private static async Task<IResult> Rematch(
        Guid caseId, RematchRequest? req, HttpContext http, UserResolver users, AppDbContext db,
        CaseWorkflowService workflow, CancellationToken ct)
    {
        if (!await IsAdminAsync(http, users, ct)) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();
        if (c.Status != CaseStatus.Escalated)
            return Results.Conflict(new { error = "Only escalated cases can be re-matched." });

        var offers = await workflow.MatchAsync(c, req?.RadiusMeters, ct);
        return Results.Ok(new { offersSent = offers, status = c.Status.ToString() });
    }
}
