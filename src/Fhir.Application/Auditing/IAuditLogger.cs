using Fhir.Domain.Auditing;

namespace Fhir.Application.Auditing;

public interface IAuditLogger
{
    /// <summary>
    /// Writes an audit event. User, hospital, IP address and correlation ID come from the current user.
    /// </summary>
    Task LogAsync(
        string action,
        string resourceType,
        string? resourceId,
        AuditOutcome outcome,
        CancellationToken cancellationToken = default);
}
