using NetTopologySuite.Geometries;

namespace DriveShield.Api.Domain;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Subject / object id from the identity provider (Entra External ID).</summary>
    public string ExternalId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>The public contact number a provider shows to customers. Not used for sign-in.</summary>
    public string? Phone { get; set; }
    /// <summary>Verified sign-in email address, lower case. Null until the person signs in with or adds one.</summary>
    public string? Email { get; set; }
    /// <summary>Verified sign-in mobile number in E.164 form (for example +919876543210). Null until added.</summary>
    public string? LoginPhone { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ProviderProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public string BusinessName { get; set; } = "";
    public string? Description { get; set; }
    public string? LicenseNumber { get; set; }
    /// <summary>Last known / base location (WGS84). X = longitude, Y = latitude.</summary>
    public Point? Location { get; set; }
    public int ServiceRadiusMeters { get; set; } = 15_000;
    public bool IsAvailable { get; set; }
    public ProviderApprovalStatus ApprovalStatus { get; set; } = ProviderApprovalStatus.Pending;
    public double AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProviderService> Services { get; set; } = [];
}

public class ProviderService
{
    public Guid ProviderId { get; set; }
    public ServiceCategory Category { get; set; }
}

public class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public string Make { get; set; } = "";
    public string Model { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public bool IsElectric { get; set; }
    public string? InsurerName { get; set; }
    public string? PolicyNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>One incident raised by a customer. The workflow state lives here.</summary>
public class Case
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Guid? VehicleId { get; set; }
    public IncidentType IncidentType { get; set; }
    public CaseStatus Status { get; set; } = CaseStatus.Reported;
    public string? Description { get; set; }
    public string? Address { get; set; }
    public Point Location { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>Maps to PostgreSQL's xmin system column: optimistic concurrency token.</summary>
    public uint Version { get; set; }

    public List<CaseCategory> RequiredCategories { get; set; } = [];
    public List<CaseProvider> Providers { get; set; } = [];
    public List<CaseStatusHistory> History { get; set; } = [];
}

public class CaseCategory
{
    public Guid CaseId { get; set; }
    public ServiceCategory Category { get; set; }
}

/// <summary>An offer of a case to a provider, for one service category.</summary>
public class CaseProvider
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CaseId { get; set; }
    public Case Case { get; set; } = null!;
    public Guid ProviderId { get; set; }
    public ProviderProfile Provider { get; set; } = null!;
    public ServiceCategory Category { get; set; }
    public OfferStatus OfferStatus { get; set; } = OfferStatus.Offered;
    public DateTimeOffset OfferedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public double DistanceMeters { get; set; }
}

/// <summary>One row per progress update: this is the timeline shown to the customer.</summary>
public class CaseStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CaseId { get; set; }
    public CaseStatus Status { get; set; }
    public string? Note { get; set; }
    public Actor Actor { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A note and/or photos added to one step of a case, by the customer, an accepted provider, or an admin.
/// Step is one of the route stops (Reported ... Reviewed).
/// </summary>
public class CaseComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CaseId { get; set; }
    public CaseStatus Step { get; set; }
    public Guid AuthorUserId { get; set; }
    public Actor Actor { get; set; }
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CasePhoto> Photos { get; set; } = [];

    /// <summary>Set when the author changes the text. Shown as "edited".</summary>
    public DateTimeOffset? EditedAt { get; set; }

    // A deleted or removed post keeps its row (so the thread still reads in order and there is an audit trail),
    // but its text and photo files are gone.
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedByUserId { get; set; }
    /// <summary>Admin = removed by the support team; anything else = deleted by its author.</summary>
    public Actor? DeletedByActor { get; set; }
    public string? RemovalReason { get; set; }
}

/// <summary>One uploaded photo. The file itself lives in the photo store under StorageKey, never in the database.</summary>
public class CasePhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CaseId { get; set; }
    public Guid CommentId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string ContentType { get; set; } = "image/jpeg";
    public int SizeBytes { get; set; }
    public string StorageKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when the photo was deleted by its author or removed by an admin. The file is deleted from the store.</summary>
    public DateTimeOffset? RemovedAt { get; set; }
    public Guid? RemovedByUserId { get; set; }
    public Actor? RemovedByActor { get; set; }
    public string? RemovalReason { get; set; }
}

public class Review
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CaseId { get; set; }
    public Guid ProviderId { get; set; }
    public Guid CustomerId { get; set; }
    public int Rating { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? Comment { get; set; }
    public string? ProviderReply { get; set; }
    public bool IsHidden { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
