namespace Fhir.Application.Hl7;

/// <summary>
/// Hands an accepted HL7 message to the processing pipeline (normally the hl7-inbound queue).
/// </summary>
public interface IHl7MessagePublisher
{
    /// <exception cref="Hl7ValidationException">
    /// Only from publishers that process in-process (development direct mode).
    /// </exception>
    Task<PublishResult> PublishAsync(string rawMessage, Hl7Header header, CancellationToken cancellationToken = default);
}

public enum PublishOutcome
{
    /// <summary>The message is durably on the queue and will be processed by the Worker.</summary>
    Queued,

    /// <summary>The message was processed in-process (development direct mode).</summary>
    Processed,

    /// <summary>The message had already been processed (development direct mode).</summary>
    Duplicate,
}

public sealed record PublishResult(PublishOutcome Outcome);
