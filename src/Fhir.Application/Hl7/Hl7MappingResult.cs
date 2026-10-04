using Hl7.Fhir.Model;

namespace Fhir.Application.Hl7;

/// <summary>
/// The FHIR resources produced from one HL7 v2 message, plus the MSH header values
/// needed for tracing and acknowledgement.
/// </summary>
/// <param name="MessageControlId">MSH-10.</param>
/// <param name="MessageType">MSH-9 as "code^trigger", e.g. "ADT^A01".</param>
/// <param name="SendingFacility">MSH-4.</param>
/// <param name="Patient">Patient mapped from PID.</param>
/// <param name="Observations">Observations mapped from OBX (empty for ADT).</param>
public sealed record Hl7MappingResult(
    string MessageControlId,
    string MessageType,
    string SendingFacility,
    Patient Patient,
    IReadOnlyList<Observation> Observations)
{
    /// <summary>All mapped resources: the Patient first, then any Observations.</summary>
    public IReadOnlyList<Resource> Resources => [Patient, .. Observations];
}
