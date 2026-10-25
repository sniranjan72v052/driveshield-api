using DriveShield.Api.Application;
using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Endpoints;

/// <summary>
/// The admin's view of the whole service: customers, providers, every request with its current status,
/// and the tools to put a provider on a request, take them off, or swap them.
/// </summary>
public static class AdminDirectoryEndpoints
{
    public static void Map(RouteGroupBuilder g)
    {
        g.MapGet("/customers", Customers);
        g.MapGet("/providers", Providers);
        g.MapGet("/cases", Cases);
        g.MapGet("/cases/{caseId:guid}", CaseDetail);
        g.MapGet("/cases/{caseId:guid}/candidates", Candidates);
        g.MapPost("/cases/{caseId:guid}/assign", Assign);
        g.MapPost("/cases/{caseId:guid}/unassign", Unassign);
    }

    private static async Task<AppUser?> AdminAsync(HttpContext http, UserResolver users, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        return user.Role == UserRole.Admin ? user : null;
    }

    /// <summary>Turns what someone typed into a case-insensitive "contains" pattern for ILIKE.</summary>
    private static string Contains(string text) =>
        "%" + text.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private static (int Skip, int Take) Paging(int? skip, int? take) => (Math.Max(0, skip ?? 0), Math.Clamp(take ?? 50, 1, 100));

    // ------------------------------------------------------------------ customers

    private static async Task<IResult> Customers(
        string? search, int? skip, int? take, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (await AdminAsync(http, users, ct) is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var (skipN, takeN) = Paging(skip, take);

        // A customer is anyone who has raised at least one request.
        var perCustomer = db.Cases.AsNoTracking()
            .GroupBy(c => c.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                Total = g.Count(),
                Open = g.Count(c => c.Status != CaseStatus.Resolved && c.Status != CaseStatus.Reviewed && c.Status != CaseStatus.Cancelled),
                Last = g.Max(c => c.CreatedAt)
            });

        var q = from s in perCustomer
                join u in db.Users.AsNoTracking() on s.CustomerId equals u.Id
                select new { u.Id, u.DisplayName, u.Email, u.LoginPhone, u.CreatedAt, s.Total, s.Open, s.Last };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = Contains(search);
            q = q.Where(x => EF.Functions.ILike(x.DisplayName, like)
                          || (x.Email != null && EF.Functions.ILike(x.Email, like))
                          || (x.LoginPhone != null && EF.Functions.ILike(x.LoginPhone, like)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(x => x.Last)
            .Skip(skipN)
            .Take(takeN)
            .ToListAsync(ct);

        var items = rows
            .Select(r => new AdminCustomerDto(r.Id, r.DisplayName, r.Email, r.LoginPhone, r.CreatedAt, r.Total, r.Open, r.Last))
            .ToList();
        return Results.Ok(new Page<AdminCustomerDto>(items, total));
    }

    // ------------------------------------------------------------------ providers

    private static async Task<IResult> Providers(
        string? search, string? approval, int? skip, int? take, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (await AdminAsync(http, users, ct) is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var (skipN, takeN) = Paging(skip, take);

        var q = db.ProviderProfiles.AsNoTracking().AsQueryable();
        if (Enum.TryParse<ProviderApprovalStatus>(approval, ignoreCase: true, out var status))
            q = q.Where(p => p.ApprovalStatus == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = Contains(search);
            q = q.Where(p => EF.Functions.ILike(p.BusinessName, like)
                          || EF.Functions.ILike(p.User.DisplayName, like)
                          || (p.User.Phone != null && EF.Functions.ILike(p.User.Phone, like))
                          || (p.User.Email != null && EF.Functions.ILike(p.User.Email, like))
                          || (p.User.LoginPhone != null && EF.Functions.ILike(p.User.LoginPhone, like)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderBy(p => p.BusinessName)
            .Skip(skipN)
            .Take(takeN)
            .Select(p => new
            {
                p.Id,
                p.BusinessName,
                Owner = p.User.DisplayName,
                p.User.Phone,
                p.User.Email,
                p.User.LoginPhone,
                p.ApprovalStatus,
                p.IsAvailable,
                Categories = p.Services.Select(s => s.Category).ToList(),
                p.AverageRating,
                p.ReviewCount,
                Active = db.CaseProviders.Count(o => o.ProviderId == p.Id && o.OfferStatus == OfferStatus.Accepted
                    && (o.Case.Status == CaseStatus.ProviderAccepted || o.Case.Status == CaseStatus.EnRoute || o.Case.Status == CaseStatus.InProgress)),
                Done = db.CaseProviders.Count(o => o.ProviderId == p.Id && o.OfferStatus == OfferStatus.Accepted
                    && (o.Case.Status == CaseStatus.Resolved || o.Case.Status == CaseStatus.Reviewed)),
                p.Location
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new AdminProviderDto(
            r.Id, r.BusinessName, r.Owner, r.Phone, r.Email, r.LoginPhone, r.ApprovalStatus.ToString(), r.IsAvailable,
            r.Categories, r.AverageRating, r.ReviewCount, r.Active, r.Done, r.Location?.Y, r.Location?.X)).ToList();
        return Results.Ok(new Page<AdminProviderDto>(items, total));
    }

    // ------------------------------------------------------------------ requests

    /// <summary>
    /// status: all (default) | open (not finished) | attention (nobody found) | active (provider on it) | resolved
    /// (resolved or reviewed) | cancelled | or one exact status name. customerId and providerId narrow it to one person.
    /// </summary>
    private static async Task<IResult> Cases(
        string? status, Guid? customerId, Guid? providerId, string? search, int? skip, int? take,
        HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (await AdminAsync(http, users, ct) is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var (skipN, takeN) = Paging(skip, take);

        var q = db.Cases.AsNoTracking().AsQueryable();
        switch (status?.Trim().ToLowerInvariant())
        {
            case null or "" or "all":
                break;
            case "open":
                q = q.Where(c => c.Status != CaseStatus.Resolved && c.Status != CaseStatus.Reviewed && c.Status != CaseStatus.Cancelled);
                break;
            case "attention":
                q = q.Where(c => c.Status == CaseStatus.Escalated);
                break;
            case "active":
                q = q.Where(c => c.Status == CaseStatus.ProviderAccepted || c.Status == CaseStatus.EnRoute || c.Status == CaseStatus.InProgress);
                break;
            case "resolved":
                q = q.Where(c => c.Status == CaseStatus.Resolved || c.Status == CaseStatus.Reviewed);
                break;
            default:
                if (!Enum.TryParse<CaseStatus>(status, ignoreCase: true, out var exact))
                    return Results.BadRequest(new { error = "Unknown status." });
                q = q.Where(c => c.Status == exact);
                break;
        }
        if (customerId is { } cid) q = q.Where(c => c.CustomerId == cid);
        if (providerId is { } pid)
            q = q.Where(c => c.Providers.Any(o => o.ProviderId == pid && o.OfferStatus == OfferStatus.Accepted));

        var joined = from c in q
                     join u in db.Users on c.CustomerId equals u.Id
                     select new { Case = c, Customer = u };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var like = Contains(search);
            joined = joined.Where(x => EF.Functions.ILike(x.Customer.DisplayName, like)
                                    || (x.Customer.Email != null && EF.Functions.ILike(x.Customer.Email, like))
                                    || (x.Customer.LoginPhone != null && EF.Functions.ILike(x.Customer.LoginPhone, like))
                                    || (x.Case.Description != null && EF.Functions.ILike(x.Case.Description, like)));
        }

        var total = await joined.CountAsync(ct);
        var page = await joined
            .OrderByDescending(x => x.Case.CreatedAt)
            .Skip(skipN)
            .Take(takeN)
            .Select(x => new
            {
                x.Case.Id,
                x.Case.Status,
                x.Case.IncidentType,
                x.Case.Description,
                x.Case.CreatedAt,
                x.Case.UpdatedAt,
                CustomerId = x.Customer.Id,
                CustomerName = x.Customer.DisplayName,
                x.Customer.Email,
                x.Customer.LoginPhone
            })
            .ToListAsync(ct);

        var ids = page.Select(r => r.Id).ToList();
        var categories = await db.CaseCategories.AsNoTracking()
            .Where(x => ids.Contains(x.CaseId))
            .Select(x => new { x.CaseId, x.Category })
            .ToListAsync(ct);
        var assigned = await db.CaseProviders.AsNoTracking()
            .Where(o => ids.Contains(o.CaseId) && o.OfferStatus == OfferStatus.Accepted)
            .Select(o => new { o.CaseId, o.Category, o.ProviderId, o.Provider.BusinessName, o.Provider.User.Phone })
            .ToListAsync(ct);
        var openOffers = await db.CaseProviders.AsNoTracking()
            .Where(o => ids.Contains(o.CaseId) && o.OfferStatus == OfferStatus.Offered)
            .GroupBy(o => o.CaseId)
            .Select(grp => new { CaseId = grp.Key, Count = grp.Count() })
            .ToListAsync(ct);

        var items = page.Select(r => new AdminCaseRowDto(
            r.Id,
            r.Status.ToString(),
            r.IncidentType.ToString(),
            r.Description,
            r.CreatedAt,
            r.UpdatedAt,
            r.CustomerId,
            string.IsNullOrWhiteSpace(r.CustomerName) ? (r.Email ?? r.LoginPhone ?? "Customer") : r.CustomerName,
            r.Email ?? r.LoginPhone,
            categories.Where(x => x.CaseId == r.Id).Select(x => x.Category).ToList(),
            assigned.Where(x => x.CaseId == r.Id)
                .Select(x => new AdminAssignmentDto(x.Category, x.ProviderId, x.BusinessName, x.Phone)).ToList(),
            openOffers.FirstOrDefault(x => x.CaseId == r.Id)?.Count ?? 0)).ToList();

        return Results.Ok(new Page<AdminCaseRowDto>(items, total));
    }

    private static async Task<IResult> CaseDetail(
        Guid caseId, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (await AdminAsync(http, users, ct) is null) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();

        var customer = await db.Users.AsNoTracking().FirstAsync(u => u.Id == c.CustomerId, ct);
        var categories = await db.CaseCategories.AsNoTracking()
            .Where(x => x.CaseId == caseId)
            .Select(x => x.Category)
            .ToListAsync(ct);
        var assigned = await db.CaseProviders.AsNoTracking()
            .Where(o => o.CaseId == caseId && o.OfferStatus == OfferStatus.Accepted)
            .Select(o => new AdminAssignmentDto(o.Category, o.ProviderId, o.Provider.BusinessName, o.Provider.User.Phone))
            .ToListAsync(ct);
        var open = await db.CaseProviders.AsNoTracking()
            .Where(o => o.CaseId == caseId && o.OfferStatus == OfferStatus.Offered)
            .GroupBy(o => o.Category)
            .Select(grp => new { Category = grp.Key, Count = grp.Count() })
            .ToListAsync(ct);

        var slots = categories
            .Select(cat => new AdminSlotDto(
                cat,
                assigned.FirstOrDefault(a => a.Category == cat),
                open.FirstOrDefault(o => o.Category == cat)?.Count ?? 0))
            .ToList();

        var vehicleRow = c.VehicleId is { } vid
            ? await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vid, ct)
            : null;

        return Results.Ok(new AdminCaseDetailDto(
            c.Id, c.Status.ToString(), c.IncidentType.ToString(), c.Description, c.Address,
            c.Location.Y, c.Location.X, c.CreatedAt, c.ResolvedAt,
            customer.Id, customer.DisplayName, customer.Email, customer.LoginPhone,
            slots, AdminAssignmentService.CanAssign(c.Status), AdminAssignmentService.CanUnassign(c.Status),
            vehicleRow is null ? null : VehicleEndpoints.ToDto(vehicleRow)));
    }

    // ------------------------------------------------------------------ who could take a request

    /// <summary>
    /// Approved providers who offer the chosen service and are within radiusKm of the request, nearest first.
    /// Typical values are 15 and 20. Providers who are switched off are included unless availableOnly is set,
    /// so support can still phone someone who has gone offline.
    /// </summary>
    private static async Task<IResult> Candidates(
        Guid caseId, ServiceCategory? category, double? radiusKm, bool? availableOnly,
        HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        if (await AdminAsync(http, users, ct) is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (category is not { } cat) return Results.BadRequest(new { error = "Choose which service to find providers for." });

        var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();

        var radiusMeters = Math.Clamp(radiusKm ?? 15, 1, 200) * 1000.0;
        var onlyAvailable = availableOnly ?? false;
        var point = c.Location;
        var customerId = c.CustomerId;

        var rows = await db.ProviderProfiles.AsNoTracking()
            .Where(p => p.ApprovalStatus == ProviderApprovalStatus.Approved
                     && p.Location != null
                     && p.UserId != customerId
                     && p.Services.Any(s => s.Category == cat)
                     && p.Location!.IsWithinDistance(point, radiusMeters)
                     && (!onlyAvailable || p.IsAvailable))
            .OrderBy(p => p.Location!.Distance(point))
            .Take(100)
            .Select(p => new
            {
                p.Id,
                p.BusinessName,
                p.User.Phone,
                Distance = p.Location!.Distance(point),
                p.IsAvailable,
                p.ServiceRadiusMeters,
                p.AverageRating,
                p.ReviewCount,
                Active = db.CaseProviders.Count(o => o.ProviderId == p.Id && o.OfferStatus == OfferStatus.Accepted
                    && (o.Case.Status == CaseStatus.ProviderAccepted || o.Case.Status == CaseStatus.EnRoute || o.Case.Status == CaseStatus.InProgress)),
                Categories = p.Services.Select(s => s.Category).ToList(),
                Assigned = db.CaseProviders.Any(o => o.CaseId == caseId && o.ProviderId == p.Id && o.Category == cat
                                                    && o.OfferStatus == OfferStatus.Accepted)
            })
            .ToListAsync(ct);

        return Results.Ok(rows.Select(r => new AdminCandidateDto(
            r.Id, r.BusinessName, r.Phone, r.Distance, r.IsAvailable, r.Distance <= r.ServiceRadiusMeters,
            r.AverageRating, r.ReviewCount, r.Active, r.Categories, r.Assigned)).ToList());
    }

    // ------------------------------------------------------------------ assign / unassign

    private static async Task<IResult> Assign(
        Guid caseId, AssignRequest req, HttpContext http, UserResolver users, AppDbContext db,
        AdminAssignmentService assignments, CancellationToken ct)
    {
        if (await AdminAsync(http, users, ct) is not { } admin) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();

        var result = await assignments.AssignAsync(c, req.Category, req.ProviderId, req.Replace ?? false, req.Note, admin.Id, ct);
        return result.ToHttp(() => Results.Ok(new { status = c.Status.ToString() }));
    }

    private static async Task<IResult> Unassign(
        Guid caseId, UnassignRequest req, HttpContext http, UserResolver users, AppDbContext db,
        AdminAssignmentService assignments, CancellationToken ct)
    {
        if (await AdminAsync(http, users, ct) is not { } admin) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();

        var result = await assignments.UnassignAsync(c, req.Category, req.Reason, admin.Id, ct);
        return result.ToHttp(() => Results.Ok(new { status = c.Status.ToString() }));
    }
}
