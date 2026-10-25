using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using DriveShield.Api.Domain;
using Microsoft.Extensions.Options;

namespace DriveShield.Api.Auth;

/// <summary>Sends a sign-in code to an email address or a phone number.</summary>
public interface IOtpSender
{
    Task SendAsync(OtpChannel channel, string destination, string code, int validMinutes, CancellationToken ct);
}

/// <summary>The email or SMS service is not set up in this environment.</summary>
public class DeliveryNotConfiguredException(string message) : Exception(message);

/// <summary>The service is set up but did not accept the message.</summary>
public class DeliveryFailedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Local development: writes the code to the API log (the Output window in Visual Studio) instead of sending anything.
/// Program.cs refuses to start with this sender outside the Development environment.
/// </summary>
public class ConsoleOtpSender(ILogger<ConsoleOtpSender> log) : IOtpSender
{
    public Task SendAsync(OtpChannel channel, string destination, string code, int validMinutes, CancellationToken ct)
    {
        log.LogWarning("DEV ONLY: sign-in code for {Destination} is {Code} (valid {Minutes} minutes)", destination, code, validMinutes);
        return Task.CompletedTask;
    }
}

/// <summary>Real delivery: email through any SMTP service, SMS through Twilio. Each channel is optional until you set it up.</summary>
public class LiveOtpSender(
    IOptions<SmtpOptions> smtp,
    IOptions<TwilioOptions> twilio,
    IHttpClientFactory httpFactory,
    ILogger<LiveOtpSender> log) : IOtpSender
{
    public Task SendAsync(OtpChannel channel, string destination, string code, int validMinutes, CancellationToken ct) =>
        channel == OtpChannel.Email
            ? SendEmailAsync(destination, code, validMinutes, ct)
            : SendSmsAsync(destination, code, validMinutes, ct);

    private async Task SendEmailAsync(string to, string code, int validMinutes, CancellationToken ct)
    {
        var o = smtp.Value;
        if (string.IsNullOrWhiteSpace(o.Host) || string.IsNullOrWhiteSpace(o.FromAddress))
            throw new DeliveryNotConfiguredException("Email sending is not set up. Fill in Email:Smtp in the API settings.");

        try
        {
            using var client = new SmtpClient(o.Host, o.Port)
            {
                EnableSsl = true, // STARTTLS on port 587, or implicit TLS where the provider requires it
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15_000
            };
            if (!string.IsNullOrEmpty(o.User)) client.Credentials = new NetworkCredential(o.User, o.Password);

            using var message = new MailMessage
            {
                From = new MailAddress(o.FromAddress, o.FromName),
                Subject = "Your DriveShield sign-in code",
                Body = $"Your DriveShield code is {code}.\n\nIt works for {validMinutes} minutes. " +
                       "If you did not ask for it, you can ignore this email: nobody can sign in without the code.",
                IsBodyHtml = false
            };
            message.To.Add(new MailAddress(to));

            await client.SendMailAsync(message, ct);
        }
        catch (Exception ex) when (ex is SmtpException or FormatException or InvalidOperationException)
        {
            throw new DeliveryFailedException("The email service did not accept the message.", ex);
        }
    }

    private async Task SendSmsAsync(string to, string code, int validMinutes, CancellationToken ct)
    {
        var o = twilio.Value;
        var hasSender = !string.IsNullOrWhiteSpace(o.From) || !string.IsNullOrWhiteSpace(o.MessagingServiceSid);
        if (string.IsNullOrWhiteSpace(o.AccountSid) || string.IsNullOrWhiteSpace(o.AuthToken) || !hasSender)
            throw new DeliveryNotConfiguredException("SMS sending is not set up. Fill in Sms:Twilio in the API settings.");

        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = $"{code} is your DriveShield code. It expires in {validMinutes} minutes."
        };
        if (!string.IsNullOrWhiteSpace(o.MessagingServiceSid)) form["MessagingServiceSid"] = o.MessagingServiceSid;
        else form["From"] = o.From;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{Uri.EscapeDataString(o.AccountSid)}/Messages.json");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{o.AccountSid}:{o.AuthToken}")));
        request.Content = new FormUrlEncodedContent(form);

        try
        {
            var client = httpFactory.CreateClient("twilio");
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // The answer can repeat the phone number, so only the status goes in the log.
                log.LogError("The SMS service answered with status {Status}", (int)response.StatusCode);
                throw new DeliveryFailedException("The SMS service did not accept the message.");
            }
        }
        catch (HttpRequestException ex)
        {
            throw new DeliveryFailedException("The SMS service could not be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new DeliveryFailedException("The SMS service took too long to answer.", ex);
        }
    }
}
