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
    IOptions<OtpOptions> otpOpts,
<<<<<<< HEAD
    IConfiguration config,
=======
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
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
<<<<<<< HEAD

        // Hosts such as Render block outbound SMTP. With Email:Brevo:ApiKey set, mail goes over HTTPS instead.
        var brevoKey = config["Email:Brevo:ApiKey"];
        if (!string.IsNullOrWhiteSpace(brevoKey))
        {
            await SendBrevoAsync(brevoKey, config["Email:Brevo:FromAddress"] ?? o.FromAddress, o.FromName, to, code, validMinutes, ct);
            return;
        }

=======
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
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
                Subject = $"Your {otpOpts.Value.AppName} sign-in code",
                Body = $"Your {otpOpts.Value.AppName} code is {code}.\n\nIt works for {validMinutes} minutes. " +
                       "If you did not ask for it, you can ignore this email: nobody can sign in without the code.",
                IsBodyHtml = false
            };
            message.To.Add(new MailAddress(to));

<<<<<<< HEAD
            // SmtpClient.Timeout is not honoured by SendMailAsync, so enforce the limit ourselves.
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(20));
            await client.SendMailAsync(message, limit.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            log.LogError("SMTP timed out connecting to {Host}:{Port}. Many hosts block outbound SMTP; use Email:Brevo:ApiKey instead.", o.Host, o.Port);
            throw new DeliveryFailedException("The email service took too long to answer.", ex);
=======
            await client.SendMailAsync(message, ct);
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
        }
        catch (Exception ex) when (ex is SmtpException or FormatException or InvalidOperationException)
        {
            log.LogError(ex, "SMTP send failed via {Host}:{Port} as {User}", o.Host, o.Port, o.User);
            throw new DeliveryFailedException("The email service did not accept the message.", ex);
        }
    }

<<<<<<< HEAD
    private async Task SendBrevoAsync(string apiKey, string fromAddress, string fromName, string to, string code, int validMinutes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fromAddress))
            throw new DeliveryNotConfiguredException("Email sending is not set up. Set Email:Smtp:FromAddress to your verified sender.");

        var payload = new
        {
            sender = new { name = fromName, email = fromAddress },
            to = new[] { new { email = to } },
            subject = $"Your {otpOpts.Value.AppName} sign-in code",
            textContent = $"Your {otpOpts.Value.AppName} code is {code}.\n\nIt works for {validMinutes} minutes. " +
                          "If you did not ask for it, you can ignore this email: nobody can sign in without the code."
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", apiKey);
        request.Content = System.Net.Http.Json.JsonContent.Create(payload);

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(15));
            var client = httpFactory.CreateClient("twilio");
            using var response = await client.SendAsync(request, limit.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                log.LogError("Brevo answered {Status}: {Body}", (int)response.StatusCode, body.Length > 300 ? body[..300] : body);
                throw new DeliveryFailedException("The email service did not accept the message.");
            }
        }
        catch (HttpRequestException ex) { throw new DeliveryFailedException("The email service could not be reached.", ex); }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) { throw new DeliveryFailedException("The email service took too long to answer.", ex); }
    }

=======
>>>>>>> ea6e7ce160a55737ca5f595cc1b53b1aa83f75a3
    private async Task SendSmsAsync(string to, string code, int validMinutes, CancellationToken ct)
    {
        var o = twilio.Value;
        var hasSender = !string.IsNullOrWhiteSpace(o.From) || !string.IsNullOrWhiteSpace(o.MessagingServiceSid);
        if (string.IsNullOrWhiteSpace(o.AccountSid) || string.IsNullOrWhiteSpace(o.AuthToken) || !hasSender)
            throw new DeliveryNotConfiguredException("SMS sending is not set up. Fill in Sms:Twilio in the API settings.");

        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = $"{code} is your {otpOpts.Value.AppName} code. It expires in {validMinutes} minutes."
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
                var body = await response.Content.ReadAsStringAsync(ct);
                var m = System.Text.RegularExpressions.Regex.Match(body, "\"message\"\\s*:\\s*\"([^\"]*)\"");
                log.LogError("The SMS service answered with status {Status}: {Reason}", (int)response.StatusCode, m.Success ? m.Groups[1].Value : "(no message)");
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
