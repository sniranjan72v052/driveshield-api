namespace DriveShield.Api.Auth;

/// <summary>Auth:Jwt in appsettings. Access tokens are short-lived; the refresh token keeps people signed in.</summary>
public class JwtOptions
{
    public string Issuer { get; set; } = "driveshield";
    public string Audience { get; set; } = "driveshield-api";

    /// <summary>
    /// Secret that signs access tokens and keys the hash of sign-in codes. At least 32 characters.
    /// Keep the real one out of source control: set the environment variable Auth__Jwt__SigningKey
    /// (in Azure, an app setting that points at Key Vault). Changing it signs everyone out.
    /// </summary>
    public string SigningKey { get; set; } = "";

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>Auth:Otp in appsettings.</summary>
public class OtpOptions
{
    /// <summary>"Console" prints codes in the API log (local development only). "Live" sends real emails and SMS.</summary>
    public string Delivery { get; set; } = "Live";

    /// <summary>Development only: also return the code in the API response so the web app can show it. Ignored elsewhere.</summary>
    public bool ExposeDevCode { get; set; }

    public int CodeLifetimeMinutes { get; set; } = 10;
    /// <summary>Wrong guesses allowed per code before it is dead.</summary>
    public int MaxAttempts { get; set; } = 5;
    public int ResendSeconds { get; set; } = 30;
    /// <summary>Codes that can be sent to one email or number per hour.</summary>
    public int MaxSendsPerHour { get; set; } = 5;
}

/// <summary>Auth section: how people are identified and who is an admin.</summary>
public class AccountOptions
{
    /// <summary>Added to numbers typed without a country code, for example 98765 43210 becomes +919876543210.</summary>
    public string DefaultCountryCode { get; set; } = "+91";

    /// <summary>
    /// Signing in with one of these (verified) emails makes the account an admin. Remove an address to
    /// take admin rights away at that person's next sign-in.
    /// </summary>
    public List<string> AdminEmails { get; set; } = [];
}

/// <summary>Email:Smtp in appsettings. Works with any SMTP service (Azure Communication Services, SendGrid, Amazon SES, Gmail app password...).</summary>
public class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "DriveShield";
}

/// <summary>Sms:Twilio in appsettings. Use either From (a number) or MessagingServiceSid.</summary>
public class TwilioOptions
{
    public string AccountSid { get; set; } = "";
    public string AuthToken { get; set; } = "";
    public string From { get; set; } = "";
    public string MessagingServiceSid { get; set; } = "";
}
