namespace DriveShield.Api.Domain;

public enum OtpChannel { Email, Phone }

/// <summary>SignIn = log in or create an account. Link = a signed-in user adding their second email or number.</summary>
public enum OtpPurpose { SignIn, Link }

/// <summary>One code sent to an email address or phone number. Only a keyed hash of the code is stored, never the code.</summary>
public class OtpChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public OtpChannel Channel { get; set; }
    /// <summary>Normalised: lower-case email, or an E.164 phone number such as +919876543210.</summary>
    public string Destination { get; set; } = "";
    public OtpPurpose Purpose { get; set; }
    /// <summary>For Link challenges: the signed-in user who is adding this email or number.</summary>
    public Guid? UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
}

/// <summary>
/// A long-lived sign-in. Only a hash of the token is stored. Each use replaces it with a new one
/// (RevokedAt + ReplacedById on the old row), so a stolen copy stops working as soon as the real app refreshes.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
}
