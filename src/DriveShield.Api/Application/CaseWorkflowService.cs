using DriveShield.Api.Domain;
using DriveShield.Api.Hubs;
using DriveShield.Api.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DriveShield.Api.Application;

public enum WorkflowOutcome { Ok, Invalid, Conflict }

public record WorkflowResult(WorkflowOutcome Outcome, string? Error = null)
{
    public bool IsOk => Outcome == WorkflowOutcome.Ok;
    public static readonly WorkflowResult Success = new(WorkflowOutcome.Ok);
    public static WorkflowResult Invalid(string error) => new(WorkflowOutcome.Invalid, error);
    public static WorkflowResult Conflict(string error) => new(WorkflowOutcome.Conflict, error);

    /// <summary>Maps the outcome to an HTTP result; onOk builds the success body.</summary>
    public IResult ToHttp(Func<IResult>? onOk = null) => Outcome switch
    {
        WorkflowOutcome.Ok => onOk?.Invoke() ?? Results.NoContent(),
        WorkflowOutcome.Conflict => Results.Conflict(new { error = Error }),
        _ => Results.BadRequest(new { error = Error })
    };
}

/// <summary>
/// Applies state-machine transitions, records the timeline entry, saves, and pushes the
/// change to everyone watching the case over SignalR.
/// </summary>
public class CaseWorkflowService(AppDbContext db, IHubContext<CaseHub> hub, IOptions<MatchingOptions> options)
{
    private readonly MatchingOptions _opts = options.Value;

    /// <summary>Saves a newly created case, then assesses and matches it to nearby providers.</summary>
    public async Task StartCaseAsync(Case c, Guid customerId, CancellationToken ct)
    {
        c.History.Add(new CaseStatusHistory
        {
            CaseId = c.Id,
            Status = CaseStatus.Reported,
            Note = "Incident reported. Help is being arranged.",
            Actor = Actor.Customer,
            ChangedByUserId = customerId
        });
        db.Cases.Add(c);
        await db.SaveChangesAsync(ct);

        var assessed = await TransitionAsync(c, CaseStatus.Assessed, Actor.System, null,
            "We have your details and are finding the right help nearby.", ct);
        if (assessed.IsOk) await MatchAsync(c, null, ct);
    }

    /// <summary>
    /// Offers the case to the nearest approved, available providers for each required category.
    /// Providers who were already asked for this case + category are skipped (used by admin re-match).
    /// </summary>
    public async Task<int> MatchAsync(Case c, double? radiusOverrideMeters, CancellationToken ct)
    {
        var categories = await db.CaseCategories
            .Where(x => x.CaseId == c.Id)
            .Select(x => x.Category)
            .ToListAsync(ct);

        var point = c.Location;
        var customerId = c.CustomerId;
        var caseId = c.Id;
        var radius = radiusOverrideMeters ?? _opts.MaxSearchRadiusMeters;
        var now = DateTimeOffset.UtcNow;
        var created = new List<CaseProvider>();

        foreach (var category in categories)
        {
            var candidates = await db.ProviderProfiles
                .Where(p => p.ApprovalStatus == ProviderApprovalStatus.Approved
                         && p.IsAvailable
                         && p.UserId != customerId
                         && p.Location != null
                         && p.Services.Any(s => s.Category == category)
                         && p.Location!.IsWithinDistance(point, radius)
                         && p.Location!.Distance(point) <= p.ServiceRadiusMeters
                         && !db.CaseProviders.Any(o => o.CaseId == caseId && o.ProviderId == p.Id && o.Category == category))
                .OrderBy(p => p.Location!.Distance(point))
                .Select(p => new { p.Id, Distance = p.Location!.Distance(point) })
                .Take(_opts.MaxOffersPerCategory)
                .ToListAsync(ct);

            foreach (var cand in candidates)
            {
                var offer = new CaseProvider
                {
                    CaseId = c.Id,
                    ProviderId = cand.Id,
                    Category = category,
                    OfferStatus = OfferStatus.Offered,
                    OfferedAt = now,
                    ExpiresAt = now.AddSeconds(_opts.OfferTtlSeconds),
                    DistanceMeters = cand.Distance
                };
                db.CaseProviders.Add(offer);
                created.Add(offer);
            }
        }

        if (created.Count == 0)
        {
            // Nobody to ask. A case that is already escalated just stays escalated.
            if (c.Status != CaseStatus.Escalated)
            {
                await TransitionAsync(c, CaseStatus.Escalated, Actor.System, null,
                    "No providers are available nearby right now. Our support team has been alerted.", ct);
            }
            return 0;
        }

        var moved = await TransitionAsync(c, CaseStatus.ProviderMatched, Actor.System, null,
            $"Your request was sent to {created.Count} nearby provider(s).", ct);
        if (!moved.IsOk) return 0;

        // Tell each provider's app about the new offer.
        foreach (var offer in created)
        {
            await hub.Clients.Group(CaseHub.ProviderGroup(offer.ProviderId)).SendAsync(
                "NewOffer",
                new NewOfferEvent(offer.Id, c.Id, offer.Category, c.IncidentType.ToString(), offer.DistanceMeters, offer.ExpiresAt),
                ct);
        }
        return created.Count;
    }

    /// <summary>Moves the case to a new status if the state machine allows it for this actor.</summary>
    public async Task<WorkflowResult> TransitionAsync(
        Case c, CaseStatus to, Actor actor, Guid? userId, string? note, CancellationToken ct)
    {
        if (!CaseStateMachine.CanTransition(c.Status, to, actor))
            return WorkflowResult.Invalid($"A {actor.ToString().ToLowerInvariant()} cannot move a case from {c.Status} to {to}.");

        var now = DateTimeOffset.UtcNow;
        c.Status = to;
        c.UpdatedAt = now;
        if (to == CaseStatus.Resolved) c.ResolvedAt = now;

        if (to == CaseStatus.Cancelled)
        {
            // Pull back any offers still waiting for an answer.
            var open = await db.CaseProviders
                .Where(o => o.CaseId == c.Id && o.OfferStatus == OfferStatus.Offered)
                .ToListAsync(ct);
            foreach (var o in open)
            {
                o.OfferStatus = OfferStatus.Withdrawn;
                o.RespondedAt = now;
            }
        }

        return await SaveAndPublishAsync(c, to, actor, userId, note, now, ct);
    }

    /// <summary>Adds a timeline entry without changing the status (e.g. a second provider joins).</summary>
    public Task<WorkflowResult> AddNoteAsync(Case c, Actor actor, Guid? userId, string note, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        c.UpdatedAt = now; // touching the row makes the xmin concurrency check apply
        return SaveAndPublishAsync(c, c.Status, actor, userId, note, now, ct);
    }

    private async Task<WorkflowResult> SaveAndPublishAsync(
        Case c, CaseStatus status, Actor actor, Guid? userId, string? note, DateTimeOffset now, CancellationToken ct)
    {
        db.CaseStatusHistory.Add(new CaseStatusHistory
        {
            CaseId = c.Id,
            Status = status,
            Note = note,
            Actor = actor,
            ChangedByUserId = userId,
            ChangedAt = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return WorkflowResult.Conflict("This case was just updated by someone else. Please refresh and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return WorkflowResult.Conflict("That action has already been taken.");
        }

        await hub.Clients.Group(CaseHub.CaseGroup(c.Id)).SendAsync(
            "CaseStatusChanged",
            new CaseStatusChangedEvent(c.Id, status.ToString(), note, actor.ToString(), now),
            ct);

        return WorkflowResult.Success;
    }
}
