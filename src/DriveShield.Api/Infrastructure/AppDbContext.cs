using DriveShield.Api.Domain;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace DriveShield.Api.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ProviderProfile> ProviderProfiles => Set<ProviderProfile>();
    public DbSet<ProviderService> ProviderServices => Set<ProviderService>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Case> Cases => Set<Case>();
    public DbSet<CaseCategory> CaseCategories => Set<CaseCategory>();
    public DbSet<CaseProvider> CaseProviders => Set<CaseProvider>();
    public DbSet<CaseStatusHistory> CaseStatusHistory => Set<CaseStatusHistory>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<CaseComment> CaseComments => Set<CaseComment>();
    public DbSet<CasePhoto> CasePhotos => Set<CasePhoto>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("postgis");

        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasIndex(x => x.ExternalId).IsUnique();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            // Sign-in identities. Each email or number belongs to at most one account. The index names match
            // db/patches/003_sign_in.sql, so a database made from the model and one upgraded by the patch are the same.
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.LoginPhone).HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique().HasFilter("email IS NOT NULL").HasDatabaseName("ux_users_email");
            e.HasIndex(x => x.LoginPhone).IsUnique().HasFilter("login_phone IS NOT NULL").HasDatabaseName("ux_users_login_phone");
        });

        b.Entity<ProviderProfile>(e =>
        {
            e.ToTable("provider_profiles");
            e.HasOne(x => x.User).WithOne().HasForeignKey<ProviderProfile>(x => x.UserId);
            e.Property(x => x.Location).HasColumnType("geography (point, 4326)");
            e.HasIndex(x => x.Location).HasMethod("gist");
            e.Property(x => x.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<ProviderService>(e =>
        {
            e.ToTable("provider_services");
            e.HasKey(x => new { x.ProviderId, x.Category });
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.HasOne<ProviderProfile>().WithMany(p => p.Services).HasForeignKey(x => x.ProviderId);
        });

        b.Entity<Vehicle>(e =>
        {
            e.ToTable("vehicles");
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId);
        });

        b.Entity<Case>(e =>
        {
            e.ToTable("cases");
            e.Property(x => x.IncidentType).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Location).HasColumnType("geography (point, 4326)");
            e.HasIndex(x => x.Location).HasMethod("gist");
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.CustomerId, x.CreatedAt });
            // uint property + IsRowVersion => Npgsql maps it to xmin
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CustomerId);
            e.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<CaseCategory>(e =>
        {
            e.ToTable("case_categories");
            e.HasKey(x => new { x.CaseId, x.Category });
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.HasOne<Case>().WithMany(c => c.RequiredCategories).HasForeignKey(x => x.CaseId);
        });

        b.Entity<CaseProvider>(e =>
        {
            e.ToTable("case_providers");
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.OfferStatus).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Case).WithMany(c => c.Providers).HasForeignKey(x => x.CaseId);
            e.HasOne(x => x.Provider).WithMany().HasForeignKey(x => x.ProviderId);
            e.HasIndex(x => new { x.CaseId, x.ProviderId, x.Category }).IsUnique();
            // database-level guarantee: only one accepted provider per case + category
            e.HasIndex(x => new { x.CaseId, x.Category }).IsUnique().HasFilter("offer_status = 'Accepted'");
            e.HasIndex(x => new { x.ProviderId, x.OfferStatus });
        });

        b.Entity<CaseStatusHistory>(e =>
        {
            e.ToTable("case_status_history");
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Actor).HasConversion<string>().HasMaxLength(20);
            e.HasOne<Case>().WithMany(c => c.History).HasForeignKey(x => x.CaseId);
            e.HasIndex(x => new { x.CaseId, x.ChangedAt });
        });

        b.Entity<CaseComment>(e =>
        {
            e.ToTable("case_comments");
            e.Property(x => x.Step).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Actor).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Body).HasMaxLength(1000);
            e.Property(x => x.DeletedByActor).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RemovalReason).HasMaxLength(300);
            e.HasOne<Case>().WithMany().HasForeignKey(x => x.CaseId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorUserId);
            e.HasIndex(x => new { x.CaseId, x.CreatedAt });
        });

        b.Entity<CasePhoto>(e =>
        {
            e.ToTable("case_photos");
            e.Property(x => x.ContentType).HasMaxLength(50);
            e.Property(x => x.StorageKey).HasMaxLength(100);
            e.Property(x => x.RemovedByActor).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RemovalReason).HasMaxLength(300);
            e.HasOne<Case>().WithMany().HasForeignKey(x => x.CaseId);
            e.HasOne<CaseComment>().WithMany(c => c.Photos).HasForeignKey(x => x.CommentId);
            e.HasIndex(x => x.CaseId);
            // the admin review queue reads photos that are still live, newest first
            e.HasIndex(x => x.CreatedAt).HasFilter("removed_at IS NULL").HasDatabaseName("ix_case_photos_active_created_at");
        });

        b.Entity<OtpChallenge>(e =>
        {
            e.ToTable("otp_challenges");
            e.Property(x => x.Channel).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Destination).HasMaxLength(254);
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.HasIndex(x => new { x.Destination, x.CreatedAt }).HasDatabaseName("ix_otp_challenges_destination_created_at");
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
            e.HasIndex(x => x.UserId).HasDatabaseName("ix_refresh_tokens_user_id");
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Review>(e =>
        {
            e.ToTable("reviews", t => t.HasCheckConstraint("ck_reviews_rating", "rating between 1 and 5"));
            e.HasOne<Case>().WithMany().HasForeignKey(x => x.CaseId);
            e.HasOne<ProviderProfile>().WithMany().HasForeignKey(x => x.ProviderId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CustomerId);
            e.HasIndex(x => new { x.CaseId, x.ProviderId }).IsUnique();
            e.HasIndex(x => x.ProviderId);
        });
    }
}

/// <summary>Helpers for WGS84 points. Note the NetTopologySuite order: X = longitude, Y = latitude.</summary>
public static class Geo
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    public static Point Point(double latitude, double longitude) =>
        Factory.CreatePoint(new Coordinate(longitude, latitude));

    public static bool IsValid(double latitude, double longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
}
