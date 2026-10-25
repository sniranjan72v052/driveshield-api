using DriveShield.Api.Application;
using DriveShield.Api.Auth;
using DriveShield.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Hubs;

/// <summary>
/// Real-time channel (path /hubs/case).
///
/// Customer app:  JoinCase(caseId)        -> receives "CaseStatusChanged" events (the live progress tracker)
/// Provider app:  JoinProviderInbox()     -> receives "NewOffer" events
///                JoinCase(caseId)        -> receives "CaseStatusChanged" for cases they are working on
///
/// Server -> client events:
///   CaseStatusChanged { caseId, status, note, actor, at }
///   NewOffer          { offerId, caseId, category, incidentType, distanceMeters, expiresAt }
/// </summary>
[Authorize]
public class CaseHub(AppDbContext db, UserResolver users) : Hub
{
    public static string CaseGroup(Guid caseId) => $"case-{caseId}";
    public static string ProviderGroup(Guid providerId) => $"provider-{providerId}";

    public async Task JoinCase(Guid caseId)
    {
        var ct = Context.ConnectionAborted;
        var user = await users.ResolveAsync(Context.User!, ct);
        var c = await db.Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == caseId, ct);
        // Same answer for "missing" and "not yours" so case ids cannot be probed.
        if (c is null) throw new HubException("Case not found.");

        var access = await CaseAccess.GetAsync(db, user, c, ct);
        if (!access.CanView) throw new HubException("Case not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, CaseGroup(caseId), ct);
    }

    public Task LeaveCase(Guid caseId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CaseGroup(caseId));

    public async Task JoinProviderInbox()
    {
        var ct = Context.ConnectionAborted;
        var user = await users.ResolveAsync(Context.User!, ct);
        var providerId = await db.ProviderProfiles
            .Where(p => p.UserId == user.Id)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (providerId is null) throw new HubException("You are not registered as a provider.");

        await Groups.AddToGroupAsync(Context.ConnectionId, ProviderGroup(providerId.Value), ct);
    }
}
