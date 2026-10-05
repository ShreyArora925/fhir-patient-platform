namespace Fhir.Domain.Auditing;

/// <summary>
/// One append-only audit record. Holds identifiers only, never clinical values or patient names.
/// </summary>
public class AuditEvent
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public required string UserId { get; set; }

    public string? DisplayName { get; set; }

    public string? HospitalCode { get; set; }

    public required string Action { get; set; }

    public required string ResourceType { get; set; }

    public string? ResourceId { get; set; }

    public AuditOutcome Outcome { get; set; }

    public string? IpAddress { get; set; }

    public string? CorrelationId { get; set; }
}
