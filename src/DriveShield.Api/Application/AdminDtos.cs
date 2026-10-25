using DriveShield.Api.Domain;

namespace DriveShield.Api.Application;

// Shapes used by the admin screens: who the customers and providers are, what each request is doing right now,
// and who could be assigned to it.

public record Page<T>(List<T> Items, int Total);

public record AdminCustomerDto(
    Guid Id,
    string Name,
    string? Email,
    string? LoginPhone,
    DateTimeOffset JoinedAt,
    int TotalCases,
    int OpenCases,
    DateTimeOffset? LastCaseAt);

public record AdminProviderDto(
    Guid Id,
    string BusinessName,
    string OwnerName,
    string? Phone,
    string? Email,
    string? LoginPhone,
    string ApprovalStatus,
    bool IsAvailable,
    List<ServiceCategory> Categories,
    double AverageRating,
    int ReviewCount,
    int ActiveJobs,
    int CompletedJobs,
    double? Latitude,
    double? Longitude);

/// <summary>The provider currently holding one service of a request.</summary>
public record AdminAssignmentDto(ServiceCategory Category, Guid ProviderId, string BusinessName, string? Phone);

/// <summary>One line of the admin request list. Assigned is empty until someone has accepted or been assigned.</summary>
public record AdminCaseRowDto(
    Guid Id,
    string Status,
    string IncidentType,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CustomerId,
    string CustomerName,
    string? CustomerContact,
    List<ServiceCategory> Categories,
    List<AdminAssignmentDto> Assigned,
    int OpenOffers);

/// <summary>One service the request needs, who has it, and how many providers are still being asked.</summary>
public record AdminSlotDto(ServiceCategory Category, AdminAssignmentDto? Assigned, int OpenOffers);

public record AdminCaseDetailDto(
    Guid Id,
    string Status,
    string IncidentType,
    string? Description,
    string? Address,
    double Latitude,
    double Longitude,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    Guid CustomerId,
    string CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    List<AdminSlotDto> Slots,
    bool CanAssign,
    bool CanUnassign,
    VehicleDto? Vehicle);

/// <summary>A provider who could take a request, with how far away they are.</summary>
public record AdminCandidateDto(
    Guid ProviderId,
    string BusinessName,
    string? Phone,
    double DistanceMeters,
    bool IsAvailable,
    bool WithinOwnRadius,
    double AverageRating,
    int ReviewCount,
    int ActiveJobs,
    List<ServiceCategory> Categories,
    bool Assigned);

public record AssignRequest(ServiceCategory Category, Guid ProviderId, bool? Replace, string? Note);
public record UnassignRequest(ServiceCategory Category, string? Reason);
