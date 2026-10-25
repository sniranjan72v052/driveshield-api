using DriveShield.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DriveShield.Api.Application;

/// <summary>Once an hour: deletes old sign-in codes and long-dead refresh tokens so those tables stay small.</summary>
public class AuthCleanupWorker(IServiceScopeFactory scopes, ILogger<AuthCleanupWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Auth cleanup failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Codes only matter for 10 minutes and for the one-hour send limit.
        var codeCutoff = DateTimeOffset.UtcNow.AddDays(-1);
        await db.OtpChallenges.Where(c => c.CreatedAt < codeCutoff).ExecuteDeleteAsync(ct);

        // Used or expired refresh tokens are kept for 30 days: reuse of one is how a stolen copy is noticed.
        var tokenCutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await db.RefreshTokens
            .Where(t => t.ExpiresAt < tokenCutoff || (t.RevokedAt != null && t.RevokedAt < tokenCutoff))
            .ExecuteDeleteAsync(ct);
    }
}
