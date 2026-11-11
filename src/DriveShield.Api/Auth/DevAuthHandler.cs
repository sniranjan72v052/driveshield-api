using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DriveShield.Api.Auth;

/// <summary>
/// LOCAL DEVELOPMENT ONLY. Lets curl and quick tests call the API without signing in with a code, by sending
/// the header  X-Dev-User: alice  (or ?dev_user=alice for SignalR). The web app does not use it.
/// Program.cs registers it only when the environment is Development.
/// </summary>
public class DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Dev";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers["X-Dev-User"].FirstOrDefault() ?? Request.Query["dev_user"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id)) return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim("sub", id), new Claim("name", id)], Scheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
