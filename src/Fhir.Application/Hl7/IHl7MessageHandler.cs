namespace Fhir.Application.Hl7;

/// <summary>
/// Stores one HL7 message: duplicate check, mapping, patient upsert, observations and the processed-message
/// record, all in a single database transaction.
/// </summary>
public interface IHl7MessageHandler
{
    /// <exception cref="Hl7ValidationException">The message has a permanent data error; do not retry.</exception>
    Task<Hl7HandleResult> HandleAsync(string rawMessage, CancellationToken cancellationToken = default);
}

public enum Hl7HandleOutcome
{
    Processed,
    Duplicate,
}

/// <param name="PatientId">The stored patient's Id. Null for duplicates.</param>
public sealed record Hl7HandleResult(
    Hl7HandleOutcome Outcome,
    string SendingFacility,
    string MessageControlId,
    Guid? PatientId = null,
    IReadOnlyList<Guid>? ObservationIds = null);
