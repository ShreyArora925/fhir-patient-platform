namespace Fhir.Application.Security;

/// <summary>
/// The caller of the current request: a person (JWT) or a hospital system (API key).
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string UserId { get; }

    string? DisplayName { get; }

    IReadOnlyCollection<string> Roles { get; }

    /// <summary>The hospital whose data the caller may see. Null means no patient data is visible.</summary>
    string? HospitalCode { get; }

    string? IpAddress { get; }

    string CorrelationId { get; }
}
