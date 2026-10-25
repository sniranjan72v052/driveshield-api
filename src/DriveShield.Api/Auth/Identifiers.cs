using System.Text;
using System.Text.RegularExpressions;
using DriveShield.Api.Domain;

namespace DriveShield.Api.Auth;

public readonly record struct ParsedIdentifier(OtpChannel Channel, string Value);

/// <summary>Turns what a person typed into one canonical email or phone number, so the same person always matches the same account.</summary>
public static class Identifiers
{
    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant);
    private static readonly Regex E164Pattern = new(@"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant);
    private static readonly Regex IndiaMobilePattern = new(@"^\+91[6-9][0-9]{9}$", RegexOptions.CultureInvariant);

    /// <summary>A text with an @ is an email; anything else is read as a phone number. Null when it is neither.</summary>
    public static ParsedIdentifier? Parse(string? raw, string? defaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        if (text.Length > 254) return null;

        if (text.Contains('@'))
        {
            var email = text.ToLowerInvariant();
            return EmailPattern.IsMatch(email) ? new ParsedIdentifier(OtpChannel.Email, email) : null;
        }

        var phone = NormalisePhone(text, defaultCountryCode);
        return phone is null ? null : new ParsedIdentifier(OtpChannel.Phone, phone);
    }

    /// <summary>
    /// "+91 98765 43210", "098765-43210" and "9876543210" all become +919876543210 (with a default country of +91).
    /// A number typed with its own + country code is kept as it is. Returns null if it cannot be a real mobile number.
    /// </summary>
    public static string? NormalisePhone(string text, string? defaultCountryCode)
    {
        var digits = new StringBuilder();
        var hasPlus = false;
        foreach (var ch in text.Trim())
        {
            if (ch is >= '0' and <= '9') digits.Append(ch);
            else if (ch == '+' && digits.Length == 0 && !hasPlus) hasPlus = true; // a plus only counts at the very start
            else if (ch is ' ' or '-' or '(' or ')' or '.') continue;
            else return null;
        }

        var d = digits.ToString();
        if (d.Length == 0) return null;

        string e164;
        if (hasPlus)
        {
            e164 = "+" + d;
        }
        else if (d.StartsWith("00", StringComparison.Ordinal))
        {
            e164 = "+" + d[2..];
        }
        else
        {
            var cc = (defaultCountryCode ?? "").Trim();
            if (!cc.StartsWith('+')) return null;
            e164 = cc + d.TrimStart('0'); // a leading 0 is the national trunk prefix, not part of the number
        }

        if (!E164Pattern.IsMatch(e164)) return null;
        if (e164.StartsWith("+91", StringComparison.Ordinal) && !IndiaMobilePattern.IsMatch(e164)) return null;
        return e164;
    }

    /// <summary>For showing back to the person without exposing the whole address: a***@gmail.com, +91 ******3210.</summary>
    public static string Mask(OtpChannel channel, string value)
    {
        if (channel == OtpChannel.Email)
        {
            var at = value.IndexOf('@');
            if (at <= 0) return "***";
            var stars = Math.Clamp(at - 1, 2, 6);
            return value[0] + new string('*', stars) + value[at..];
        }

        if (value.Length < 8) return "****";
        return value[..3] + " ******" + value[^4..];
    }
}
