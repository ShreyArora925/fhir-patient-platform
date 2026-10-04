namespace Fhir.Application.Hl7;

/// <summary>
/// A permanent data error in an HL7 message. Retrying the same message will not succeed.
/// </summary>
public sealed class Hl7ValidationException : Exception
{
    public Hl7ValidationException(string message)
        : base(message)
    {
    }

    public Hl7ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
