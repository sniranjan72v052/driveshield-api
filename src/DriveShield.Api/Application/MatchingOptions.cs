namespace DriveShield.Api.Application;

public class MatchingOptions
{
    /// <summary>How far from the incident we look for providers.</summary>
    public double MaxSearchRadiusMeters { get; set; } = 25_000;

    /// <summary>How many providers per service category receive an offer.</summary>
    public int MaxOffersPerCategory { get; set; } = 5;

    /// <summary>How long a provider has to accept before the offer expires.</summary>
    public int OfferTtlSeconds { get; set; } = 180;
}
