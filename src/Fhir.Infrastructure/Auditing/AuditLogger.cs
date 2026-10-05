using Fhir.Application.Auditing;
using Fhir.Application.Security;
using Fhir.Domain.Auditing;
using Fhir.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Fhir.Infrastructure.Auditing;

public sealed class AuditLogger(
    FhirDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<AuditLogger> logger) : IAuditLogger
{
    public async Task LogAsync(
        string action,
        string resourceType,
        string? resourceId,
        AuditOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent
        {
            TimestampUtc = timeProvider.GetUtcNow().UtcDateTime,
            UserId = currentUser.UserId,
            DisplayName = currentUser.DisplayName,
            HospitalCode = currentUser.HospitalCode,
            Action = action,
            ResourceType = resourceType,
            ResourceId = Truncate(resourceId, MaxResourceIdLength),
            Outcome = outcome,
            IpAddress = currentUser.IpAddress,
            CorrelationId = currentUser.CorrelationId,
        };

        db.AuditEvents.Add(auditEvent);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Audit {Action} {ResourceType}/{ResourceId} by {UserId} ({HospitalCode}): {Outcome}",
            action,
            resourceType,
            resourceId,
            currentUser.UserId,
            currentUser.HospitalCode,
            outcome);
    }

    // Resource IDs can come from request input (e.g. an unparseable id), so keep them within the column size.
    private const int MaxResourceIdLength = 200;

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;
}
