using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Fhir.Application.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Fhir.Api.Security;

/// <summary>
/// Authenticates hospital systems by the X-Api-Key header. Each configured key maps to one HospitalCode.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    IOptionsMonitor<IngestOptions> ingestOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presentedKey = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(presentedKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var hospitalCode = FindHospital(presentedKey);
        if (hospitalCode is null)
        {
            Logger.LogWarning("Rejected ingest request with an unknown API key from {IpAddress}", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Unknown API key."));
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, $"system:{hospitalCode}"),
                new Claim(ClaimTypes.Name, $"Hospital system {hospitalCode}"),
                new Claim(ClaimTypes.Role, Roles.HospitalSystem),
                new Claim(ClaimNames.Hospital, hospitalCode),
            ],
            SchemeName);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? FindHospital(string presentedKey)
    {
        // Compare hashes in constant time so response timing does not leak how much of a key matched.
        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
        string? match = null;

        foreach (var entry in ingestOptions.CurrentValue.ApiKeys)
        {
            if (string.IsNullOrEmpty(entry.Key) || string.IsNullOrEmpty(entry.HospitalCode))
            {
                continue;
            }

            var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(entry.Key));
            if (CryptographicOperations.FixedTimeEquals(presentedHash, configuredHash))
            {
                match = entry.HospitalCode;
            }
        }

        return match;
    }
}

/// <summary>Bound from the "Ingest" configuration section.</summary>
public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    public List<IngestApiKey> ApiKeys { get; set; } = [];
}

public sealed class IngestApiKey
{
    public string Key { get; set; } = string.Empty;

    public string HospitalCode { get; set; } = string.Empty;
}
