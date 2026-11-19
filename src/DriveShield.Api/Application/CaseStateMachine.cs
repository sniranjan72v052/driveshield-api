using DriveShield.Api.Domain;
using static DriveShield.Api.Domain.CaseStatus;

namespace DriveShield.Api.Application;

/// <summary>
/// The allowed case transitions and who may perform them.
/// This is the single place that encodes the workflow shown to customers.
///
///  Reported -> Assessed -> ProviderMatched -> ProviderAccepted -> EnRoute -> InProgress -> Resolved -> Reviewed
///  (Cancelled from most early states; Escalated when nobody accepts or a provider cannot continue)
/// </summary>
public static class CaseStateMachine
{
    private static readonly Dictionary<(CaseStatus From, CaseStatus To), Actor[]> Rules = new()
    {
        [(Reported, Assessed)]               = [Actor.System, Actor.Admin],
        [(Reported, Cancelled)]              = [Actor.Customer, Actor.Admin],

        [(Assessed, ProviderMatched)]        = [Actor.System, Actor.Admin],
        [(Assessed, Escalated)]              = [Actor.System, Actor.Admin],
        [(Assessed, Cancelled)]              = [Actor.Customer, Actor.Admin],

        [(ProviderMatched, ProviderAccepted)] = [Actor.Provider, Actor.System],
        [(ProviderMatched, Escalated)]       = [Actor.System, Actor.Admin],
        [(ProviderMatched, Cancelled)]       = [Actor.Customer, Actor.Admin],

        [(ProviderAccepted, EnRoute)]        = [Actor.Provider],
        [(ProviderAccepted, Escalated)]      = [Actor.Provider, Actor.Admin],
        [(ProviderAccepted, Cancelled)]      = [Actor.Customer, Actor.Admin],

        [(EnRoute, InProgress)]              = [Actor.Provider],
        [(EnRoute, Escalated)]               = [Actor.Provider, Actor.Admin],
        [(EnRoute, Cancelled)]               = [Actor.Customer, Actor.Admin],

        [(InProgress, Resolved)]             = [Actor.Provider, Actor.Admin],
        [(InProgress, Escalated)]            = [Actor.Provider, Actor.Admin],

        [(Resolved, Reviewed)]               = [Actor.System],

        [(Escalated, ProviderMatched)]       = [Actor.Admin, Actor.System],
        [(Escalated, Cancelled)]             = [Actor.Admin],
    };

    public static bool CanTransition(CaseStatus from, CaseStatus to, Actor actor) =>
        Rules.TryGetValue((from, to), out var actors) && actors.Contains(actor);

    /// <summary>Statuses this actor may move the case to right now (drives UI buttons).</summary>
    public static IReadOnlyList<CaseStatus> NextStatuses(CaseStatus from, Actor actor) =>
        Rules.Where(r => r.Key.From == from && r.Value.Contains(actor))
             .Select(r => r.Key.To)
             .ToList();

    public static bool IsTerminal(CaseStatus status) => status is Reviewed or Cancelled;

    /// <summary>States in which providers can still accept or work on the case.</summary>
    public static bool IsActive(CaseStatus status) =>
        status is ProviderMatched or ProviderAccepted or EnRoute or InProgress;
}
