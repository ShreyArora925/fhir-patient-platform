using System.Globalization;
using Fhir.Application.Hl7;
using Hl7.Fhir.Model;
using NHapi.Base;
using NHapi.Base.Model;
using NHapi.Base.Parser;
using NHapi.Model.V25.Datatype;
using NHapi.Model.V25.Message;
using NHapi.Model.V25.Segment;

namespace Fhir.Infrastructure.Hl7;

/// <summary>
/// Maps HL7 v2.5 ADT^A01 and ORU^R01 messages to FHIR R4 Patient and Observation resources.
/// </summary>
public sealed class Hl7ToFhirMapper : IHl7ToFhirMapper
{
    private const string LoincSystem = "http://loinc.org";
    private const string UcumSystem = "http://unitsofmeasure.org";
    private const string IdentifierTypeSystem = "http://terminology.hl7.org/CodeSystem/v2-0203";
    private const string ObservationCategorySystem = "http://terminology.hl7.org/CodeSystem/observation-category";
    private const string InterpretationSystem = "http://terminology.hl7.org/CodeSystem/v3-ObservationInterpretation";

    private static readonly string[] Hl7DateFormats = ["yyyyMMdd", "yyyyMM", "yyyy"];

    private readonly PipeParser _parser = new();

    public Hl7MappingResult Map(string rawMessage)
    {
        var message = Parse(rawMessage);
        var msh = (MSH)message.GetStructure("MSH");

        var controlId = msh.MessageControlID.Value;
        if (string.IsNullOrWhiteSpace(controlId))
        {
            throw new Hl7ValidationException("MSH-10 (message control ID) is missing.");
        }

        var code = msh.MessageType.MessageCode.Value;
        var trigger = msh.MessageType.TriggerEvent.Value;
        var messageType = $"{code}^{trigger}";
        var sendingFacility = msh.SendingFacility.NamespaceID.Value ?? string.Empty;

        return (message, messageType) switch
        {
            (ADT_A01 adt, "ADT^A01") => new Hl7MappingResult(
                controlId, messageType, sendingFacility, MapPatient(adt.PID), []),
            (ORU_R01 oru, "ORU^R01") => MapOru(oru, controlId, messageType, sendingFacility),
            _ => throw new Hl7ValidationException(
                $"Unsupported message type '{messageType}' in message {controlId}. Supported types: ADT^A01, ORU^R01."),
        };
    }

    private IMessage Parse(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
        {
            throw new Hl7ValidationException("HL7 message is empty.");
        }

        // HL7 requires \r between segments, but files edited on other platforms often use \n or \r\n.
        var normalized = rawMessage.Trim().Replace("\r\n", "\r").Replace('\n', '\r');

        try
        {
            return _parser.Parse(normalized);
        }
        catch (HL7Exception ex)
        {
            throw new Hl7ValidationException($"HL7 message could not be parsed: {ex.Message}", ex);
        }
    }

    private static Hl7MappingResult MapOru(ORU_R01 oru, string controlId, string messageType, string sendingFacility)
    {
        if (oru.PATIENT_RESULTRepetitionsUsed == 0)
        {
            throw new Hl7ValidationException($"PID-3 (patient identifier) is missing in message {controlId}.");
        }

        var patientResult = oru.GetPATIENT_RESULT(0);
        var patient = MapPatient(patientResult.PATIENT.PID);

        var observations = new List<Observation>();
        for (var o = 0; o < patientResult.ORDER_OBSERVATIONRepetitionsUsed; o++)
        {
            var order = patientResult.GetORDER_OBSERVATION(o);
            for (var i = 0; i < order.OBSERVATIONRepetitionsUsed; i++)
            {
                observations.Add(MapObservation(order.GetOBSERVATION(i).OBX, patient));
            }
        }

        return new Hl7MappingResult(controlId, messageType, sendingFacility, patient, observations);
    }

    private static Patient MapPatient(PID pid)
    {
        var patient = new Patient { Id = Guid.NewGuid().ToString() };

        foreach (var cx in pid.GetPatientIdentifierList())
        {
            if (!string.IsNullOrWhiteSpace(cx.IDNumber.Value))
            {
                patient.Identifier.Add(MapIdentifier(cx));
            }
        }

        if (patient.Identifier.Count == 0)
        {
            throw new Hl7ValidationException("PID-3 (patient identifier) is missing.");
        }

        foreach (var xpn in pid.GetPatientName())
        {
            var name = MapName(xpn);
            if (name is not null)
            {
                patient.Name.Add(name);
            }
        }

        patient.BirthDate = MapBirthDate(pid.DateTimeOfBirth.Time.Value);
        patient.Gender = MapGender(pid.AdministrativeSex.Value);

        foreach (var xad in pid.GetPatientAddress())
        {
            var address = MapAddress(xad);
            if (address is not null)
            {
                patient.Address.Add(address);
            }
        }

        foreach (var xtn in pid.GetPhoneNumberHome())
        {
            var telecom = MapTelecom(xtn);
            if (telecom is not null)
            {
                patient.Telecom.Add(telecom);
            }
        }

        return patient;
    }

    private static Identifier MapIdentifier(CX cx)
    {
        var identifier = new Identifier { Value = cx.IDNumber.Value };

        var typeCode = cx.IdentifierTypeCode.Value;
        if (!string.IsNullOrWhiteSpace(typeCode))
        {
            identifier.Type = new CodeableConcept(IdentifierTypeSystem, typeCode);
        }

        var authority = cx.AssigningAuthority.NamespaceID.Value;
        if (!string.IsNullOrWhiteSpace(authority))
        {
            identifier.Assigner = new ResourceReference { Display = authority };
        }

        return identifier;
    }

    private static HumanName? MapName(XPN xpn)
    {
        var family = xpn.FamilyName.Surname.Value;
        var given = new[] { xpn.GivenName.Value, xpn.SecondAndFurtherGivenNamesOrInitialsThereof.Value }
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .ToList();

        if (string.IsNullOrWhiteSpace(family) && given.Count == 0)
        {
            return null;
        }

        var name = new HumanName { Family = family, Given = given };

        if (!string.IsNullOrWhiteSpace(xpn.SuffixEgJRorIII.Value))
        {
            name.Suffix = [xpn.SuffixEgJRorIII.Value];
        }

        if (!string.IsNullOrWhiteSpace(xpn.PrefixEgDR.Value))
        {
            name.Prefix = [xpn.PrefixEgDR.Value];
        }

        return name;
    }

    private static string? MapBirthDate(string? hl7Date)
    {
        if (string.IsNullOrWhiteSpace(hl7Date))
        {
            return null;
        }

        // TS may carry a time and offset (e.g. 19880514083000-0500); only the date part matters here.
        var datePart = hl7Date.Length > 8 ? hl7Date[..8] : hl7Date;

        if (!DateTime.TryParseExact(datePart, Hl7DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new Hl7ValidationException($"PID-7 (date of birth) '{hl7Date}' is not a valid HL7 date (expected YYYYMMDD).");
        }

        return datePart.Length switch
        {
            8 => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            6 => date.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            _ => date.ToString("yyyy", CultureInfo.InvariantCulture),
        };
    }

    private static AdministrativeGender? MapGender(string? sex) => sex?.Trim().ToUpperInvariant() switch
    {
        "M" => AdministrativeGender.Male,
        "F" => AdministrativeGender.Female,
        "O" or "A" or "N" => AdministrativeGender.Other,
        "U" => AdministrativeGender.Unknown,
        _ => null,
    };

    private static Address? MapAddress(XAD xad)
    {
        var lines = new[] { xad.StreetAddress.StreetOrMailingAddress.Value, xad.OtherDesignation.Value }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        var address = new Address
        {
            Line = lines,
            City = NullIfBlank(xad.City.Value),
            State = NullIfBlank(xad.StateOrProvince.Value),
            PostalCode = NullIfBlank(xad.ZipOrPostalCode.Value),
            Country = NullIfBlank(xad.Country.Value),
            Use = xad.AddressType.Value switch
            {
                "H" => Address.AddressUse.Home,
                "B" or "O" => Address.AddressUse.Work,
                "C" => Address.AddressUse.Temp,
                _ => null,
            },
        };

        var isEmpty = lines.Count == 0 && address.City is null && address.State is null
            && address.PostalCode is null && address.Country is null;

        return isEmpty ? null : address;
    }

    private static ContactPoint? MapTelecom(XTN xtn)
    {
        var equipment = xtn.TelecommunicationEquipmentType.Value;
        var useCode = xtn.TelecommunicationUseCode.Value;

        string? value;
        ContactPoint.ContactPointSystem system;

        if (equipment == "Internet" || useCode == "NET")
        {
            value = NullIfBlank(xtn.EmailAddress.Value) ?? NullIfBlank(xtn.TelephoneNumber.Value);
            system = ContactPoint.ContactPointSystem.Email;
        }
        else
        {
            // XTN-1 is deprecated in v2.5 but still widely used; fall back to area code + local number.
            value = NullIfBlank(xtn.TelephoneNumber.Value)
                ?? NullIfBlank($"{xtn.AreaCityCode.Value}{xtn.LocalNumber.Value}");
            system = equipment == "FX" ? ContactPoint.ContactPointSystem.Fax : ContactPoint.ContactPointSystem.Phone;
        }

        if (value is null)
        {
            return null;
        }

        return new ContactPoint
        {
            System = system,
            Value = value,
            Use = useCode switch
            {
                "PRN" => ContactPoint.ContactPointUse.Home,
                "WPN" => ContactPoint.ContactPointUse.Work,
                "ORN" or "VHN" => ContactPoint.ContactPointUse.Temp,
                _ => equipment == "CP" ? ContactPoint.ContactPointUse.Mobile : null,
            },
        };
    }

    private static Observation MapObservation(OBX obx, Patient patient)
    {
        var coding = obx.ObservationIdentifier;
        var codeSystem = coding.NameOfCodingSystem.Value == "LN" ? LoincSystem : coding.NameOfCodingSystem.Value;

        var observation = new Observation
        {
            Id = Guid.NewGuid().ToString(),
            Status = MapObservationStatus(obx.ObservationResultStatus.Value),
            Category =
            [
                new CodeableConcept(ObservationCategorySystem, "laboratory", "Laboratory"),
            ],
            Code = new CodeableConcept(codeSystem, coding.Identifier.Value, coding.Text.Value, coding.Text.Value),
            Subject = new ResourceReference($"Patient/{patient.Id}")
            {
                Identifier = patient.Identifier.FirstOrDefault(),
            },
        };

        var unit = obx.Units.Identifier.Value;
        var rawValue = obx.ObservationValueRepetitionsUsed > 0
            ? (obx.GetObservationValue(0).Data as IPrimitive)?.Value
            : null;

        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            observation.Value = obx.ValueType.Value == "NM"
                ? new Quantity
                {
                    Value = ParseDecimal(rawValue, "OBX-5 (observation value)"),
                    Unit = unit,
                    System = UcumSystem,
                    Code = unit,
                }
                : new FhirString(rawValue);
        }

        var range = MapReferenceRange(obx.ReferencesRange.Value, unit);
        if (range is not null)
        {
            observation.ReferenceRange.Add(range);
        }

        foreach (var flag in obx.GetAbnormalFlags())
        {
            if (!string.IsNullOrWhiteSpace(flag.Value))
            {
                observation.Interpretation.Add(new CodeableConcept(InterpretationSystem, flag.Value));
            }
        }

        var effective = obx.DateTimeOfTheObservation.Time.Value;
        if (!string.IsNullOrWhiteSpace(effective) && effective.Length >= 8)
        {
            observation.Effective = new FhirDateTime(ToFhirDateTime(effective));
        }

        return observation;
    }

    private static Observation.ReferenceRangeComponent? MapReferenceRange(string? range, string? unit)
    {
        if (string.IsNullOrWhiteSpace(range))
        {
            return null;
        }

        var parts = range.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var low)
            && decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var high))
        {
            return new Observation.ReferenceRangeComponent
            {
                Low = new Quantity { Value = low, Unit = unit, System = UcumSystem, Code = unit },
                High = new Quantity { Value = high, Unit = unit, System = UcumSystem, Code = unit },
            };
        }

        return new Observation.ReferenceRangeComponent { Text = range };
    }

    private static ObservationStatus MapObservationStatus(string? status) => status switch
    {
        "P" or "S" => ObservationStatus.Preliminary,
        "C" => ObservationStatus.Corrected,
        "D" or "W" => ObservationStatus.EnteredInError,
        "X" => ObservationStatus.Cancelled,
        "R" or "I" => ObservationStatus.Registered,
        _ => ObservationStatus.Final,
    };

    private static decimal ParseDecimal(string value, string field)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
        {
            throw new Hl7ValidationException($"{field} '{value}' is not a valid number.");
        }

        return result;
    }

    private static string ToFhirDateTime(string hl7)
    {
        // YYYYMMDD[HHMM[SS]][+/-ZZZZ] -> YYYY-MM-DD[THH:MM:SS+ZZ:ZZ]. FHIR requires a timezone whenever a
        // time is present, so without an offset in the source only the date is kept.
        var date = $"{hl7[..4]}-{hl7[4..6]}-{hl7[6..8]}";

        var offsetIndex = hl7.IndexOfAny(['+', '-']);
        if (offsetIndex < 0 || hl7.Length - offsetIndex != 5)
        {
            return date;
        }

        var time = hl7[8..offsetIndex].Split('.')[0];
        if (time.Length < 4)
        {
            return date;
        }

        var seconds = time.Length >= 6 ? time[4..6] : "00";
        var offset = $"{hl7[offsetIndex..(offsetIndex + 3)]}:{hl7[(offsetIndex + 3)..]}";
        return $"{date}T{time[..2]}:{time[2..4]}:{seconds}{offset}";
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
