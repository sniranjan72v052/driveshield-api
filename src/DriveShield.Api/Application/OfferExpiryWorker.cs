using DriveShield.Api.Domain;
using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Application;

/// <summary>
/// Every 20 seconds: expires offers nobody answered, and escalates cases where no provider
/// is left to answer. In production this can move to an Azure Function on a timer trigger.
/// Running on several API instances is safe: the case's xmin concurrency check stops double transitions.
/// </summary>
public class OfferExpiryWorker(IServiceScopeFactory scopes, ILogger<OfferExpiryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Offer expiry sweep failed");
            }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var workflow = scope.ServiceProvider.GetRequiredService<CaseWorkflowService>();
        var now = DateTimeOffset.UtcNow;

        var expired = await db.CaseProviders
            .Where(o => o.OfferStatus == OfferStatus.Offered && o.ExpiresAt < now)
            .ToListAsync(ct);
        foreach (var o in expired)
        {
            o.OfferStatus = OfferStatus.Expired;
            o.RespondedAt = now;
        }
        if (expired.Count > 0) await db.SaveChangesAsync(ct);

        var stuck = await db.Cases
            .Where(c => c.Status == CaseStatus.ProviderMatched
                     && !c.Providers.Any(p => p.OfferStatus == OfferStatus.Offered || p.OfferStatus == OfferStatus.Accepted))
            .ToListAsync(ct);
        foreach (var c in stuck)
        {
            await workflow.TransitionAsync(c, CaseStatus.Escalated, Actor.System, null,
                "No provider was able to accept in time. Our support team is finding help for you.", ct);
        }
    }
}
