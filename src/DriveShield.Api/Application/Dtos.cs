using DriveShield.Api.Domain;

namespace DriveShield.Api.Application;

// ------------------------------------------------------------ requests
public record CreateCaseRequest(
    IncidentType IncidentType,
    double Latitude,
    double Longitude,
    string? Description,
    string? Address,
    Guid? VehicleId,
    List<ServiceCategory>? Categories);

public record StatusUpdateRequest(CaseStatus Status, string? Note);

public record RegisterProviderRequest(
    string BusinessName,
    string? Description,
    string? LicenseNumber,
    string? Phone,
    List<ServiceCategory> Categories,
    double Latitude,
    double Longitude,
    int? ServiceRadiusMeters);

public record LocationRequest(double Latitude, double Longitude);
public record AvailabilityRequest(bool IsAvailable);
public record ReviewRequest(int Rating, string? Comment, List<string>? Tags);
public record RematchRequest(double? RadiusMeters);
public record EditCommentRequest(string? Body);
public record UpdateMeRequest(string? DisplayName);
public record VehicleRequest(string? Make, string? Model, string? RegistrationNumber, bool? IsElectric, string? InsurerName, string? PolicyNumber);

// ------------------------------------------------------------ responses
public record TimelineEntryDto(Guid Id, string Status, string? Note, string Actor, DateTimeOffset At);

public record ProviderOnCaseDto(
    Guid ProviderId,
    string BusinessName,
    string? Phone,
    ServiceCategory Category,
    string OfferStatus,
    double AverageRating,
    int ReviewCount,
    bool Reviewed);

public record CaseDetailDto(
    Guid Id,
    string Status,
    string IncidentType,
    string? Description,
    string? Address,
    double Latitude,
    double Longitude,
    bool LocationIsApproximate,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    List<string> AllowedNextStatuses,
    List<ProviderOnCaseDto> Providers,
    List<TimelineEntryDto> Timeline,
    bool CanComment,
    List<CommentDto> Comments,
    VehicleDto? Vehicle);

/// <summary>One photo. When Removed is set ("Author" or "Support") the file is gone and only a placeholder remains.</summary>
public record PhotoDto(Guid Id, string ContentType, string? Removed, string? RemovalReason, bool CanRemove);

/// <summary>
/// A note and/or photos on one step. AuthorLabel is "Customer", the provider's business name, or "Support team".
/// Removed is null, "Author" or "Support": a removed post keeps its place as a placeholder.
/// RemovalReason is only ever filled in for admins. Mine, CanEdit and CanDelete are worked out for the person asking,
/// so the apps never have to repeat the rules.
/// </summary>
public record CommentDto(
    Guid Id,
    string Step,
    string Actor,
    string AuthorLabel,
    string? Body,
    DateTimeOffset At,
    List<PhotoDto> Photos,
    DateTimeOffset? EditedAt,
    string? Removed,
    string? RemovalReason,
    bool Mine,
    bool CanEdit,
    bool CanDelete);

public record CaseSummaryDto(Guid Id, string Status, string IncidentType, DateTimeOffset CreatedAt);

public record OfferDto(
    Guid OfferId,
    Guid CaseId,
    ServiceCategory Category,
    string IncidentType,
    string? Description,
    double ApproxLatitude,
    double ApproxLongitude,
    double DistanceMeters,
    DateTimeOffset ExpiresAt);

public record NearbyProviderDto(
    Guid Id,
    string BusinessName,
    double DistanceMeters,
    double AverageRating,
    int ReviewCount,
    List<ServiceCategory> Categories);

public record VehicleDto(Guid Id, string Make, string Model, string RegistrationNumber, bool IsElectric, string? InsurerName, string? PolicyNumber);

/// <summary>VehicleCount lets the apps ask a new customer for their vehicle once, and not ask again.</summary>
public record MeDto(Guid Id, string DisplayName, string Role, Guid? ProviderId, string? Email, string? LoginPhone, int VehicleCount);

public record ProviderProfileDto(
    Guid Id,
    string BusinessName,
    string ApprovalStatus,
    bool IsAvailable,
    int ServiceRadiusMeters,
    List<ServiceCategory> Categories,
    double AverageRating,
    int ReviewCount,
    double? Latitude,
    double? Longitude);

public record JobDto(
    Guid CaseId,
    string Status,
    string IncidentType,
    ServiceCategory Category,
    string? Description,
    DateTimeOffset CreatedAt);

/// <summary>One photo in the admin review queue.</summary>
public record AdminPhotoDto(Guid PhotoId, Guid CaseId, string IncidentType, string Step, string Actor, DateTimeOffset At);

// ------------------------------------------------------------ SignalR events
public record CaseStatusChangedEvent(Guid CaseId, string Status, string? Note, string Actor, DateTimeOffset At);
public record CommentAddedEvent(Guid CaseId, string Step);
public record NewOfferEvent(Guid OfferId, Guid CaseId, ServiceCategory Category, string IncidentType, double DistanceMeters, DateTimeOffset ExpiresAt);
