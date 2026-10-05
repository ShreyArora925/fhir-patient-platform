namespace Fhir.Domain.Messaging;

/// <summary>
/// Records every HL7 message that has been stored. Its key (SendingFacility, MessageControlId) is the duplicate check.
/// </summary>
public class ProcessedMessage
{
    public required string SendingFacility { get; set; }

    public required string MessageControlId { get; set; }

    public required string MessageType { get; set; }

    public DateTime ReceivedUtc { get; set; }

    public required string RawHl7 { get; set; }
}
