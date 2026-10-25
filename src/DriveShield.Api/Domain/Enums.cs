using System.Text.RegularExpressions;

namespace DriveShield.Api.Domain;

public enum UserRole { Customer, Provider, Admin }

/// <summary>The four incident types from the flow diagram.</summary>
public enum IncidentType { Accident, Breakdown, NaturalCalamity, WrongParking }

/// <summary>The service branches from the flow diagram.</summary>
public enum ServiceCategory
{
    InsuranceClaimHelp,
    TowingOnSite,
    RoadsideAssistance,
    LegalSupport,
    WorkshopGuidance,
    EvVehicleAdvice,
    WarrantyAssistance
}

public enum ProviderApprovalStatus { Pending, Approved, Rejected, Suspended }

/// <summary>The progress steps the customer sees.</summary>
public enum CaseStatus
{
    Reported,
    Assessed,
    ProviderMatched,
    ProviderAccepted,
    EnRoute,
    InProgress,
    Resolved,
    Reviewed,
    Cancelled,
    Escalated
}

public enum OfferStatus { Offered, Accepted, Declined, Expired, Withdrawn }

/// <summary>Who is performing a status change. System = automated (matching, expiry, review).</summary>
public enum Actor { Customer, Provider, Admin, System }

public static class EnumText
{
    /// <summary>"TowingOnSite" -> "Towing On Site"</summary>
    public static string Humanize<T>(this T value) where T : struct, Enum =>
        Regex.Replace(value.ToString(), "(?<!^)([A-Z])", " $1");
}
