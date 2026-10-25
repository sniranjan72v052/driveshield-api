using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Application;

/// <summary>What the current user is allowed to see and do on one case.</summary>
public record CaseAccess(bool IsAdmin, bool IsCustomer, Guid? ProviderId, bool HasAnyOffer, bool HasAcceptedOffer)
{
    /// <summary>Admins, the customer, and providers holding a live or accepted offer.</summary>
    public bool CanView => IsAdmin || IsCustomer || HasAnyOffer;

    /// <summary>Role used by the state machine. Null = not allowed to change the case.</summary>
    public Actor? ActingAs =>
        IsAdmin ? Actor.Admin :
        IsCustomer ? Actor.Customer :
        HasAcceptedOffer ? Actor.Provider :
        null;

    public static async Task<CaseAccess> GetAsync(AppDbContext db, AppUser user, Case c, CancellationToken ct)
    {
        var providerId = await db.ProviderProfiles
            .Where(p => p.UserId == user.Id)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        var anyOffer = false;
        var accepted = false;
        if (providerId is not null)
        {
            var statuses = await db.CaseProviders
                .Where(o => o.CaseId == c.Id && o.ProviderId == providerId)
                .Select(o => o.OfferStatus)
                .ToListAsync(ct);
            anyOffer = statuses.Any(s => s is OfferStatus.Offered or OfferStatus.Accepted);
            accepted = statuses.Contains(OfferStatus.Accepted);
        }

        return new CaseAccess(
            IsAdmin: user.Role == UserRole.Admin,
            IsCustomer: c.CustomerId == user.Id,
            ProviderId: providerId,
            HasAnyOffer: anyOffer,
            HasAcceptedOffer: accepted);
    }
}
