namespace Fhir.Application.Hl7;

/// <summary>
/// The MSH values needed before full parsing: enough to validate, route and de-duplicate a message.
/// </summary>
public sealed record Hl7Header(string SendingFacility, string MessageControlId, string MessageType)
{
    public const int MaxSendingFacilityLength = 64;
    public const int MaxMessageControlIdLength = 199;

    /// <summary>
    /// Reads MSH-4, MSH-9 and MSH-10 without a full HL7 parse. Returns false with a reason if the message
    /// does not start with an MSH segment or has no MSH-10.
    /// </summary>
    public static bool TryParse(string? rawMessage, out Hl7Header? header, out string? error)
    {
        header = null;
        error = null;

        var message = rawMessage?.TrimStart() ?? string.Empty;
        if (message.Length == 0)
        {
            error = "HL7 message is empty.";
            return false;
        }

        if (!message.StartsWith("MSH", StringComparison.Ordinal) || message.Length < 8)
        {
            error = "HL7 message must start with an MSH segment.";
            return false;
        }

        // MSH-1 is the field separator itself and MSH-2 holds the encoding characters, so after splitting
        // the segment on the field separator, MSH-n is at index n - 1.
        var fieldSeparator = message[3];
        var componentSeparator = message[4];
        var segmentEnd = message.IndexOfAny(['\r', '\n']);
        var msh = segmentEnd < 0 ? message : message[..segmentEnd];
        var fields = msh.Split(fieldSeparator);

        string Field(int number) => fields.Length > number - 1 ? fields[number - 1].Trim() : string.Empty;
        string FirstComponent(int number) => Field(number).Split(componentSeparator)[0].Trim();

        var controlId = Field(10);
        if (controlId.Length == 0)
        {
            error = "MSH-10 (message control ID) is missing.";
            return false;
        }

        if (controlId.Length > MaxMessageControlIdLength)
        {
            error = $"MSH-10 (message control ID) is longer than {MaxMessageControlIdLength} characters.";
            return false;
        }

        var sendingFacility = FirstComponent(4);
        if (sendingFacility.Length > MaxSendingFacilityLength)
        {
            error = $"MSH-4 (sending facility) is longer than {MaxSendingFacilityLength} characters.";
            return false;
        }

        var typeParts = Field(9).Split(componentSeparator);
        var messageType = typeParts.Length > 1 ? $"{typeParts[0]}^{typeParts[1]}" : typeParts[0];

        header = new Hl7Header(sendingFacility, controlId, messageType);
        return true;
    }
}
