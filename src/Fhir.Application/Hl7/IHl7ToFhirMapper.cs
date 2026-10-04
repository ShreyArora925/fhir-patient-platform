namespace Fhir.Application.Hl7;

/// <summary>
/// Converts a raw HL7 v2 message into FHIR R4 resources.
/// </summary>
public interface IHl7ToFhirMapper
{
    /// <summary>
    /// Parses and maps a raw HL7 v2 message.
    /// </summary>
    /// <exception cref="Hl7ValidationException">
    /// The message is malformed, missing required data, or of an unsupported type.
    /// </exception>
    Hl7MappingResult Map(string rawMessage);
}
