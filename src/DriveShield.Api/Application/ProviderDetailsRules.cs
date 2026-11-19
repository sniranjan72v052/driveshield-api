using System.Text.RegularExpressions;
using DriveShield.Api.Domain;

namespace DriveShield.Api.Application;

/// <summary>Checks and cleans the business details a provider types, then copies them onto the profile.</summary>
public static class ProviderDetailsRules
{
    private static readonly Regex Pin = new(@"^[1-9][0-9]{5}$", RegexOptions.CultureInvariant);
    private static readonly Regex Pan = new(@"^[A-Z]{5}[0-9]{4}[A-Z]$", RegexOptions.CultureInvariant);
    private static readonly Regex Ifsc = new(@"^[A-Z]{4}0[A-Z0-9]{6}$", RegexOptions.CultureInvariant);
    private static readonly Regex Account = new(@"^[0-9]{9,18}$", RegexOptions.CultureInvariant);
    private static readonly Regex Email = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);

    /// <summary>Returns an error message, or null after the details were copied onto the profile (the caller saves).</summary>
    public static string? Apply(ProviderProfile p, ProviderDetails d)
    {
        string? error = null;

        string? Text(string? v, int max, string label)
        {
            var t = v?.Trim();
            if (string.IsNullOrEmpty(t)) return null;
            if (t.Length > max) { error ??= $"{label} is too long (max {max} characters)."; return null; }
            return t;
        }

        string? Pattern(string? v, Regex re, string label, string hint, bool upper)
        {
            var t = v?.Replace(" ", "").Trim();
            if (string.IsNullOrEmpty(t)) return null;
            if (upper) t = t.ToUpperInvariant();
            if (!re.IsMatch(t)) { error ??= $"{label} is not valid. {hint}"; return null; }
            return t;
        }

        decimal? Percent(decimal? v, string label)
        {
            if (v is null) return null;
            if (v < 0 || v > 100) { error ??= $"{label} must be between 0 and 100."; return null; }
            return Math.Round(v.Value, 2);
        }

        var license = Text(d.LicenseNumber, 100, "Licence or GST No");
        var address = Text(d.Address, 300, "Address");
        var city = Text(d.City, 100, "City");
        var state = Text(d.State, 100, "State");
        var pin = Pattern(d.PinCode, Pin, "Pin code", "It has 6 digits.", false);
        var manager = Text(d.ManagerName, 100, "Manager name");
        var managerEmail = Text(d.ManagerEmail, 254, "Manager email")?.ToLowerInvariant();
        if (managerEmail is not null && !Email.IsMatch(managerEmail)) error ??= "Manager email is not valid.";
        var pan = Pattern(d.PanNumber, Pan, "PAN number", "It looks like ABCDE1234F.", true);
        var account = Pattern(d.BankAccountNumber, Account, "Bank account number", "It has 9 to 18 digits.", false);
        var ifsc = Pattern(d.IfscCode, Ifsc, "IFSC code", "It looks like HDFC0001234.", true);
        var tds = Percent(d.TdsPercentage, "TDS percentage");
        var zone = Text(d.Zone, 50, "Zone");
        var paint = Percent(d.PaintDiscount, "Paint discount");
        var paintOem = Percent(d.PaintDiscountOem, "Paint discount OEM");
        var imd = Text(d.ImdCode, 30, "IMD code");

        if (error is not null) return error;

        p.LicenseNumber = license;
        p.Address = address;
        p.City = city;
        p.State = state;
        p.PinCode = pin;
        p.ManagerName = manager;
        p.ManagerEmail = managerEmail;
        p.PanNumber = pan;
        p.BankAccountNumber = account;
        p.IfscCode = ifsc;
        p.TdsPercentage = tds;
        p.Zone = zone;
        p.PaintDiscount = paint;
        p.PaintDiscountOem = paintOem;
        p.ImdCode = imd;
        return null;
    }
}
