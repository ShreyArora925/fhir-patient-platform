namespace Fhir.Domain.Auditing;

public enum AuditOutcome
{
    /// <summary>The action was allowed and completed.</summary>
    Success,

    /// <summary>The action was refused for authorization reasons (e.g. another hospital's data).</summary>
    Denied,

    /// <summary>The action was allowed but could not complete (bad input, not found, downstream failure).</summary>
    Failed,
}
