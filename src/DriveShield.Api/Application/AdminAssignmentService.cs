using DriveShield.Api.Domain;
using DriveShield.Api.Hubs;
using DriveShield.Api.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using static DriveShield.Api.Domain.CaseStatus;

namespace DriveShield.Api.Application;

/// <summary>
/// Lets support put a provider on a request, take them off again, or swap them for someone else.
/// Every change is written to the timeline the customer sees, and the case status follows the people on it.
/// </summary>
public class AdminAssignmentService(AppDbContext db, CaseWorkflowService workflow, IHubContext<CaseHub> hub)
{
    private const int MaxNoteLength = 300;

    /// <summary>A request can be assigned or reassigned until the work is done.</summary>
    public static bool CanAssign(CaseStatus s) => s is Assessed or ProviderMatched or ProviderAccepted or EnRoute or InProgress or Escalated;

    /// <summary>Someone can be taken off only while they hold the request (accepted, travelling or working).</summary>
    public static bool CanUnassign(CaseStatus s) => s is ProviderAccepted or EnRoute or InProgress;

    public async Task<WorkflowResult> AssignAsync(
        Case c, ServiceCategory category, Guid providerId, bool replace, string? adminNote, Guid adminId, CancellationToken ct)
    {
        if (adminNote?.Length > MaxNoteLength)
            return WorkflowResult.Invalid($"The note is too long (max {MaxNoteLength} characters).");
        if (!CanAssign(c.Status))
            return WorkflowResult.Conflict($"A request that is {c.Status.Humanize().ToLowerInvariant()} cannot be assigned.");

        if (!await db.CaseCategories.AnyAsync(x => x.CaseId == c.Id && x.Category == category, ct))
            return WorkflowResult.Invalid("This request does not need that service.");

        var provider = await db.ProviderProfiles
            .Include(p => p.Services)
            .FirstOrDefaultAsync(p => p.Id == providerId, ct);
        if (provider is null) return WorkflowResult.Invalid("Unknown provider.");
        if (provider.ApprovalStatus != ProviderApprovalStatus.Approved)
            return WorkflowResult.Invalid("Only approved providers can be assigned.");
        if (provider.UserId == c.CustomerId)
            return WorkflowResult.Invalid("A provider cannot be assigned to their own request.");
        if (provider.Services.All(s => s.Category != category))
            return WorkflowResult.Invalid($"{provider.BusinessName} does not offer {category.Humanize().ToLowerInvariant()}.");

        var now = DateTimeOffset.UtcNow;

        var current = await db.CaseProviders
            .Include(o => o.Provider)
            .FirstOrDefaultAsync(o => o.CaseId == c.Id && o.Category == category && o.OfferStatus == OfferStatus.Accepted, ct);
        var replacedName = current?.Provider.BusinessName;
        if (current is not null)
        {
            if (current.ProviderId == providerId)
                return WorkflowResult.Conflict($"{provider.BusinessName} is already assigned to this service.");
            if (!replace)
                return WorkflowResult.Conflict(
                    $"{current.Provider.BusinessName} is already assigned to this service. Unassign them first, or choose reassign.");

            // Save this first: only one provider may hold a service at a time (a unique index enforces it),
            // so the old one has to be released before the new one is recorded.
            current.OfferStatus = OfferStatus.Withdrawn;
            current.RespondedAt = now;
            await db.SaveChangesAsync(ct);
        }

        var point = c.Location;
        var distance = await db.ProviderProfiles
            .Where(p => p.Id == providerId && p.Location != null)
            .Select(p => (double?)p.Location!.Distance(point))
            .FirstOrDefaultAsync(ct) ?? 0;

        // Re-use an earlier row for the same provider and service (there is one per case, provider and service).
        var row = await db.CaseProviders.FirstOrDefaultAsync(
            o => o.CaseId == c.Id && o.ProviderId == providerId && o.Category == category, ct);
        if (row is null)
        {
            row = new CaseProvider
            {
                CaseId = c.Id,
                ProviderId = providerId,
                Category = category,
                OfferedAt = now,
                DistanceMeters = distance
            };
            db.CaseProviders.Add(row);
        }
        row.OfferStatus = OfferStatus.Accepted;
        row.RespondedAt = now;
        row.ExpiresAt = now;
        row.DistanceMeters = distance;

        // Anyone else still being asked for this service is no longer needed.
        var rivals = await db.CaseProviders
            .Where(o => o.CaseId == c.Id && o.Category == category && o.Id != row.Id && o.OfferStatus == OfferStatus.Offered)
            .ToListAsync(ct);
        foreach (var r in rivals)
        {
            r.OfferStatus = OfferStatus.Withdrawn;
            r.RespondedAt = now;
        }

        var note = $"{provider.BusinessName} was assigned to your {category.Humanize().ToLowerInvariant()} request by our support team.";
        if (!string.IsNullOrWhiteSpace(adminNote)) note += " " + adminNote.Trim();

        var otherAcceptedRemain = await db.CaseProviders.AnyAsync(
            o => o.CaseId == c.Id && o.OfferStatus == OfferStatus.Accepted && o.Id != row.Id, ct);

        var result = await SettleStatusAsync(c, replacedName is not null, otherAcceptedRemain, note, adminId, ct);
        if (!result.IsOk) return result;

        // Tell the provider's app so the job appears at once (the page also refreshes every 15 seconds).
        await hub.Clients.Group(CaseHub.ProviderGroup(providerId)).SendAsync(
            "NewOffer",
            new NewOfferEvent(row.Id, c.Id, category, c.IncidentType.ToString(), distance, now),
            ct);
        return WorkflowResult.Success;
    }

    /// <summary>
    /// Moves the case to "provider accepted" the way the customer expects to see it. A swapped-in provider has not
    /// started yet, so a case that was already travelling or in progress is put back first.
    /// </summary>
    private async Task<WorkflowResult> SettleStatusAsync(
        Case c, bool replaced, bool otherAcceptedRemain, string note, Guid adminId, CancellationToken ct)
    {
        switch (c.Status)
        {
            case ProviderAccepted:
                return await workflow.AddNoteAsync(c, Actor.Admin, adminId, note, ct);

            case EnRoute or InProgress when !replaced || otherAcceptedRemain:
                // Another provider is already on the way or working, so the case keeps its place on the route.
                return await workflow.AddNoteAsync(c, Actor.Admin, adminId, note, ct);
        }

        if (c.Status is EnRoute or InProgress)
        {
            var back = await workflow.TransitionAsync(c, Escalated, Actor.Admin, adminId,
                "The previous provider was taken off this request by our support team.", ct);
            if (!back.IsOk) return back;
        }

        if (c.Status is Escalated or Assessed)
        {
            var matched = await workflow.TransitionAsync(c, ProviderMatched, Actor.Admin, adminId,
                "Our support team is arranging a provider for you.", ct);
            if (!matched.IsOk) return matched;
        }

        return await workflow.TransitionAsync(c, ProviderAccepted, Actor.System, adminId, note, ct);
    }

    public async Task<WorkflowResult> UnassignAsync(
        Case c, ServiceCategory category, string? reason, Guid adminId, CancellationToken ct)
    {
        if (reason?.Length > MaxNoteLength)
            return WorkflowResult.Invalid($"The reason is too long (max {MaxNoteLength} characters).");
        if (!CanUnassign(c.Status))
            return WorkflowResult.Conflict("A provider can only be taken off a request that is accepted and not yet resolved.");

        var current = await db.CaseProviders
            .Include(o => o.Provider)
            .FirstOrDefaultAsync(o => o.CaseId == c.Id && o.Category == category && o.OfferStatus == OfferStatus.Accepted, ct);
        if (current is null) return WorkflowResult.Conflict("Nobody is assigned to that service.");

        current.OfferStatus = OfferStatus.Withdrawn;
        current.RespondedAt = DateTimeOffset.UtcNow;

        var anyoneLeft = await db.CaseProviders.AnyAsync(
            o => o.CaseId == c.Id && o.OfferStatus == OfferStatus.Accepted && o.Id != current.Id, ct);

        var note = $"{current.Provider.BusinessName} was taken off your {category.Humanize().ToLowerInvariant()} request by our support team.";
        note += anyoneLeft ? "" : " We are finding another provider for you.";
        if (!string.IsNullOrWhiteSpace(reason)) note += " " + reason.Trim();

        return anyoneLeft
            ? await workflow.AddNoteAsync(c, Actor.Admin, adminId, note, ct)
            : await workflow.TransitionAsync(c, Escalated, Actor.Admin, adminId, note, ct);
    }
}
