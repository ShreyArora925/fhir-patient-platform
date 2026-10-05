using Fhir.Application.Security;

namespace Fhir.Worker;

/// <summary>
/// The Worker has no request user. HospitalCode is null, so any hospital-filtered query sees nothing;
/// message handling uses the explicit system context instead.
/// </summary>
internal sealed class WorkerCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public string UserId => "system:worker";

    public string? DisplayName => "Fhir.Worker";

    public IReadOnlyCollection<string> Roles => [];

    public string? HospitalCode => null;

    public string? IpAddress => null;

    public string CorrelationId => string.Empty;
}
