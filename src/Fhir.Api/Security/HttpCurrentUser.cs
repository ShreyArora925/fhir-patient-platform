using System.Security.Claims;
using Fhir.Application.Security;

namespace Fhir.Api.Security;

/// <summary>
/// The current user from the request's ClaimsPrincipal. Works for JWT users (dotnet user-jwts or Entra ID)
/// and for hospital systems authenticated by API key.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public const string CorrelationIdHeader = "X-Correlation-ID";
    private const int MaxCorrelationIdLength = 128;

    private HttpContext? Context => httpContextAccessor.HttpContext;

    private ClaimsPrincipal? Principal => Context?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string UserId =>
        IsAuthenticated
            ? FindFirst(ClaimTypes.NameIdentifier, "sub", "oid") ?? "unknown"
            : "anonymous";

    public string? DisplayName => FindFirst("name", ClaimTypes.Name, "preferred_username");

    public IReadOnlyCollection<string> Roles =>
        Principal?.Claims
            .Where(c => c.Type is ClaimTypes.Role or "role" or "roles")
            .Select(c => c.Value)
            .Distinct()
            .ToArray() ?? [];

    public string? HospitalCode => IsAuthenticated ? FindFirst(ClaimNames.Hospital) : null;

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();

    public string CorrelationId
    {
        get
        {
            var header = Context?.Request.Headers[CorrelationIdHeader].ToString();
            if (!string.IsNullOrWhiteSpace(header) && header.Length <= MaxCorrelationIdLength)
            {
                return header;
            }

            return Context?.TraceIdentifier ?? string.Empty;
        }
    }

    private string? FindFirst(params string[] claimTypes) =>
        claimTypes
            .Select(type => Principal?.FindFirst(type)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
