using DriveShield.Api.Application;
using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Endpoints;

public static class CaseEndpoints
{
    public static void MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/cases").RequireAuthorization();

        g.MapPost("/", CreateCase);
        g.MapGet("/mine", MyCases);
        g.MapGet("/{caseId:guid}", GetCase);
        g.MapGet("/{caseId:guid}/timeline", GetTimeline);
        g.MapPost("/{caseId:guid}/status", UpdateStatus);
        g.MapPost("/{caseId:guid}/offers/{offerId:guid}/accept", AcceptOffer);
        g.MapPost("/{caseId:guid}/offers/{offerId:guid}/decline", DeclineOffer);
        g.MapPost("/{caseId:guid}/providers/{providerId:guid}/review", SubmitReview);
    }

    /// <summary>Default services for each incident type. The customer can override by sending Categories.</summary>
    private static List<ServiceCategory> DefaultCategories(IncidentType type) => type switch
    {
        IncidentType.Accident => [ServiceCategory.TowingOnSite, ServiceCategory.InsuranceClaimHelp],
        IncidentType.Breakdown => [ServiceCategory.RoadsideAssistance],
        IncidentType.NaturalCalamity => [ServiceCategory.TowingOnSite, ServiceCategory.InsuranceClaimHelp],
        IncidentType.WrongParking => [ServiceCategory.LegalSupport, ServiceCategory.TowingOnSite],
        _ => [ServiceCategory.RoadsideAssistance]
    };

    // ------------------------------------------------------------------ create / list

    private static async Task<IResult> CreateCase(
        CreateCaseRequest req, HttpContext http, UserResolver users, AppDbContext db,
        CaseWorkflowService workflow, CancellationToken ct)
    {
        if (!Geo.IsValid(req.Latitude, req.Longitude))
            return Results.BadRequest(new { error = "Latitude or longitude is out of range." });
        if (req.Description?.Length > 2000)
            return Results.BadRequest(new { error = "Description is too long (max 2000 characters)." });

        var user = await users.ResolveAsync(http.User, ct);

        if (req.VehicleId is { } vehicleId &&
            !await db.Vehicles.AnyAsync(v => v.Id == vehicleId && v.OwnerId == user.Id, ct))
            return Results.BadRequest(new { error = "Unknown vehicle." });

        var categories = (req.Categories is { Count: > 0 } ? req.Categories : DefaultCategories(req.IncidentType))
            .Distinct()
            .ToList();

        var c = new Case
        {
            CustomerId = user.Id,
            VehicleId = req.VehicleId,
            IncidentType = req.IncidentType,
            Description = req.Description?.Trim(),
            Address = req.Address?.Trim(),
            Location = Geo.Point(req.Latitude, req.Longitude),
            RequiredCategories = categories.Select(x => new CaseCategory { Category = x }).ToList()
        };

        await workflow.StartCaseAsync(c, user.Id, ct);
        return Results.Created($"/api/cases/{c.Id}", new { id = c.Id, status = c.Status.ToString() });
    }

    private static async Task<IResult> MyCases(HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var rows = await db.Cases.AsNoTracking()
            .Where(c => c.CustomerId == user.Id)
            .OrderByDescending(c => c.CreatedAt)
            .Take(100)
            .Select(c => new { c.Id, c.Status, c.IncidentType, c.CreatedAt })
            .ToListAsync(ct);

        return Results.Ok(rows.Select(r =>
            new CaseSummaryDto(r.Id, r.Status.ToString(), r.IncidentType.ToString(), r.CreatedAt)));
    }

    // ------------------------------------------------------------------ read

    private static async Task<IResult> GetCase(
        Guid caseId, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();

        var access = await CaseAccess.GetAsync(db, user, c, ct);
        if (!access.CanView) return Results.NotFound(); // same answer as "missing": no probing

        var offers = await db.CaseProviders.AsNoTracking()
            .Include(o => o.Provider).ThenInclude(p => p.User)
            .Where(o => o.CaseId == caseId)
            .ToListAsync(ct);

        // Customers see the providers who accepted; admins see everyone; a provider sees only themselves.
        var visible = offers.Where(o =>
            access.IsAdmin ||
            (access.IsCustomer && o.OfferStatus == OfferStatus.Accepted) ||
            (!access.IsCustomer && o.ProviderId == access.ProviderId)).ToList();

        var reviewedProviders = await db.Reviews.AsNoTracking()
            .Where(r => r.CaseId == caseId)
            .Select(r => r.ProviderId)
            .ToListAsync(ct);

        var providers = visible.Select(o => new ProviderOnCaseDto(
            o.ProviderId,
            o.Provider.BusinessName,
            o.OfferStatus == OfferStatus.Accepted ? o.Provider.User.Phone : null,
            o.Category,
            o.OfferStatus.ToString(),
            o.Provider.AverageRating,
            o.Provider.ReviewCount,
            reviewedProviders.Contains(o.ProviderId))).ToList();

        var timeline = await LoadTimelineAsync(db, caseId, ct);

        // Photos and comments are visible only to the customer, admins, and providers who accepted the case.
        var comments = access.ActingAs is not null
            ? await CommentEndpoints.LoadCommentsAsync(db, caseId, user.Id, access.IsAdmin, c.Status, ct)
            : new List<CommentDto>();

        // A provider who has only been offered the case sees an approximate location (~100 m).
        var exact = access.IsAdmin || access.IsCustomer || access.HasAcceptedOffer;
        var lat = exact ? c.Location.Y : Math.Round(c.Location.Y, 3);
        var lng = exact ? c.Location.X : Math.Round(c.Location.X, 3);

        var next = access.ActingAs is { } actor
            ? CaseStateMachine.NextStatuses(c.Status, actor).Select(s => s.ToString()).ToList()
            : [];

        // The vehicle is shared with the customer, support, and providers who have taken the request: not with those only offered it.
        VehicleDto? vehicle = null;
        if (exact && c.VehicleId is { } vehicleId)
        {
            var v = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == vehicleId, ct);
            if (v is not null) vehicle = VehicleEndpoints.ToDto(v);
        }

        return Results.Ok(new CaseDetailDto(
            c.Id, c.Status.ToString(), c.IncidentType.ToString(), c.Description, c.Address,
            lat, lng, !exact, c.CreatedAt, c.ResolvedAt, next, providers, timeline,
            access.ActingAs is not null, comments, vehicle));
    }

    private static async Task<IResult> GetTimeline(
        Guid caseId, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();

        var access = await CaseAccess.GetAsync(db, user, c, ct);
        if (!access.CanView) return Results.NotFound();

        return Results.Ok(await LoadTimelineAsync(db, caseId, ct));
    }

    private static async Task<List<TimelineEntryDto>> LoadTimelineAsync(AppDbContext db, Guid caseId, CancellationToken ct)
    {
        var rows = await db.CaseStatusHistory.AsNoTracking()
            .Where(h => h.CaseId == caseId)
            .OrderBy(h => h.ChangedAt)
            .ToListAsync(ct);
        return rows.Select(h => new TimelineEntryDto(h.Id, h.Status.ToString(), h.Note, h.Actor.ToString(), h.ChangedAt)).ToList();
    }

    // ------------------------------------------------------------------ progress updates

    /// <summary>Provider/customer/admin moves the case forward (or cancels). The state machine decides what is allowed.</summary>
    private static async Task<IResult> UpdateStatus(
        Guid caseId, StatusUpdateRequest req, HttpContext http, UserResolver users, AppDbContext db,
        CaseWorkflowService workflow, CancellationToken ct)
    {
        if (req.Note?.Length > 500)
            return Results.BadRequest(new { error = "Note is too long (max 500 characters)." });

        var user = await users.ResolveAsync(http.User, ct);
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();

        var access = await CaseAccess.GetAsync(db, user, c, ct);
        if (!access.CanView) return Results.NotFound();
        if (access.ActingAs is not { } actor) return Results.StatusCode(StatusCodes.Status403Forbidden);

        var result = await workflow.TransitionAsync(c, req.Status, actor, user.Id, req.Note?.Trim(), ct);
        return result.ToHttp(() => Results.Ok(new { status = c.Status.ToString() }));
    }

    // ------------------------------------------------------------------ offers

    private static async Task<IResult> AcceptOffer(
        Guid caseId, Guid offerId, HttpContext http, UserResolver users, AppDbContext db,
        CaseWorkflowService workflow, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var offer = await db.CaseProviders
            .Include(o => o.Provider)
            .Include(o => o.Case)
            .FirstOrDefaultAsync(o => o.Id == offerId && o.CaseId == caseId, ct);
        if (offer is null || offer.Provider.UserId != user.Id) return Results.NotFound();

        if (offer.Provider.ApprovalStatus != ProviderApprovalStatus.Approved)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        var now = DateTimeOffset.UtcNow;
        if (offer.OfferStatus != OfferStatus.Offered || offer.ExpiresAt < now || !CaseStateMachine.IsActive(offer.Case.Status))
            return Results.Conflict(new { error = "This offer is no longer available." });

        var taken = await db.CaseProviders.AnyAsync(o =>
            o.CaseId == caseId && o.Category == offer.Category && o.OfferStatus == OfferStatus.Accepted, ct);
        if (taken) return Results.Conflict(new { error = "Another provider has already accepted this request." });

        offer.OfferStatus = OfferStatus.Accepted;
        offer.RespondedAt = now;

        // First to accept wins: withdraw the other open offers for the same service.
        var rivals = await db.CaseProviders
            .Where(o => o.CaseId == caseId && o.Category == offer.Category && o.Id != offerId && o.OfferStatus == OfferStatus.Offered)
            .ToListAsync(ct);
        foreach (var r in rivals)
        {
            r.OfferStatus = OfferStatus.Withdrawn;
            r.RespondedAt = now;
        }

        var note = $"{offer.Provider.BusinessName} accepted your {offer.Category.Humanize().ToLowerInvariant()} request.";
        var result = offer.Case.Status == CaseStatus.ProviderMatched
            ? await workflow.TransitionAsync(offer.Case, CaseStatus.ProviderAccepted, Actor.Provider, user.Id, note, ct)
            : await workflow.AddNoteAsync(offer.Case, Actor.Provider, user.Id, note, ct);

        return result.ToHttp(() => Results.Ok(new { status = offer.Case.Status.ToString() }));
    }

    private static async Task<IResult> DeclineOffer(
        Guid caseId, Guid offerId, HttpContext http, UserResolver users, AppDbContext db, CancellationToken ct)
    {
        var user = await users.ResolveAsync(http.User, ct);
        var offer = await db.CaseProviders
            .Include(o => o.Provider)
            .FirstOrDefaultAsync(o => o.Id == offerId && o.CaseId == caseId, ct);
        if (offer is null || offer.Provider.UserId != user.Id) return Results.NotFound();
        if (offer.OfferStatus != OfferStatus.Offered)
            return Results.Conflict(new { error = "This offer is no longer open." });

        offer.OfferStatus = OfferStatus.Declined;
        offer.RespondedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        // If this was the last open offer, OfferExpiryWorker escalates the case on its next sweep.
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ reviews

    private static async Task<IResult> SubmitReview(
        Guid caseId, Guid providerId, ReviewRequest req, HttpContext http, UserResolver users, AppDbContext db,
        CaseWorkflowService workflow, CancellationToken ct)
    {
        if (req.Rating is < 1 or > 5) return Results.BadRequest(new { error = "Rating must be between 1 and 5." });
        if (req.Comment?.Length > 2000) return Results.BadRequest(new { error = "Comment is too long (max 2000 characters)." });
        if (req.Tags?.Count > 10 || req.Tags?.Any(t => t.Length > 40) == true)
            return Results.BadRequest(new { error = "Too many or too long tags." });

        var user = await users.ResolveAsync(http.User, ct);
        var c = await db.Cases.FirstOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || c.CustomerId != user.Id) return Results.NotFound();

        if (c.Status is not (CaseStatus.Resolved or CaseStatus.Reviewed))
            return Results.Conflict(new { error = "You can review after the service is resolved." });

        var acceptedProviderIds = await db.CaseProviders
            .Where(o => o.CaseId == caseId && o.OfferStatus == OfferStatus.Accepted)
            .Select(o => o.ProviderId)
            .Distinct()
            .ToListAsync(ct);
        if (!acceptedProviderIds.Contains(providerId))
            return Results.BadRequest(new { error = "That provider did not work on this case." });

        if (await db.Reviews.AnyAsync(r => r.CaseId == caseId && r.ProviderId == providerId, ct))
            return Results.Conflict(new { error = "You have already reviewed this provider for this case." });

        db.Reviews.Add(new Review
        {
            CaseId = caseId,
            ProviderId = providerId,
            CustomerId = user.Id,
            Rating = req.Rating,
            Comment = req.Comment?.Trim(),
            Tags = req.Tags?.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList() ?? []
        });

        // Once every provider who worked on the case has been reviewed, the case becomes Reviewed.
        var reviewedCount = await db.Reviews.CountAsync(r => r.CaseId == caseId, ct) + 1;
        var allReviewed = reviewedCount >= acceptedProviderIds.Count;

        var result = allReviewed && c.Status == CaseStatus.Resolved
            ? await workflow.TransitionAsync(c, CaseStatus.Reviewed, Actor.System, user.Id, "Thank you for your review.", ct)
            : await workflow.AddNoteAsync(c, Actor.Customer, user.Id, "Review submitted.", ct);
        if (!result.IsOk) return result.ToHttp();

        // Recompute the provider's aggregate from the source rows (safe under concurrent reviews).
        var stats = await db.Reviews
            .Where(r => r.ProviderId == providerId && !r.IsHidden)
            .GroupBy(r => 1)
            .Select(g => new { Average = g.Average(r => (double)r.Rating), Count = g.Count() })
            .FirstAsync(ct);
        await db.ProviderProfiles
            .Where(p => p.Id == providerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.AverageRating, stats.Average)
                .SetProperty(p => p.ReviewCount, stats.Count), ct);

        return Results.Created($"/api/cases/{caseId}", new { average = Math.Round(stats.Average, 2), count = stats.Count });
    }
}
