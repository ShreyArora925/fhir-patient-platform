using Fhir.Application.Hl7;
using Fhir.Infrastructure.Hl7;
using Hl7.Fhir.Model;

namespace Fhir.UnitTests.Hl7;

public class Hl7ToFhirMapperTests
{
    private readonly Hl7ToFhirMapper _mapper = new();

    private static string LoadSample(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "hl7", fileName));

    [Fact]
    public void Map_AdtA01_ReturnsHeaderValues()
    {
        var result = _mapper.Map(LoadSample("ADT_A01.hl7"));

        Assert.Equal("MSG00001", result.MessageControlId);
        Assert.Equal("ADT^A01", result.MessageType);
        Assert.Equal("TGH", result.SendingFacility);
    }

    [Fact]
    public void Map_AdtA01_ReturnsOnlyPatient()
    {
        var result = _mapper.Map(LoadSample("ADT_A01.hl7"));

        Assert.Empty(result.Observations);
        var resource = Assert.Single(result.Resources);
        Assert.Same(result.Patient, resource);
    }

    [Fact]
    public void Map_AdtA01_MapsPatientIdentifier()
    {
        var patient = _mapper.Map(LoadSample("ADT_A01.hl7")).Patient;

        var identifier = Assert.Single(patient.Identifier);
        Assert.Equal("MRN12345", identifier.Value);
        Assert.Equal("MR", identifier.Type!.Coding[0].Code);
        Assert.Equal("TGH", identifier.Assigner!.Display);
    }

    [Fact]
    public void Map_AdtA01_MapsPatientName()
    {
        var patient = _mapper.Map(LoadSample("ADT_A01.hl7")).Patient;

        var name = Assert.Single(patient.Name);
        Assert.Equal("Doe", name.Family);
        Assert.Equal(["Jane", "Marie"], name.Given);
    }

    [Fact]
    public void Map_AdtA01_MapsBirthDateAndGender()
    {
        var patient = _mapper.Map(LoadSample("ADT_A01.hl7")).Patient;

        Assert.Equal("1988-05-14", patient.BirthDate);
        Assert.Equal(AdministrativeGender.Female, patient.Gender);
    }

    [Fact]
    public void Map_AdtA01_MapsAddress()
    {
        var patient = _mapper.Map(LoadSample("ADT_A01.hl7")).Patient;

        var address = Assert.Single(patient.Address);
        Assert.Equal(["123 Queen St W", "Apt 4B"], address.Line);
        Assert.Equal("Toronto", address.City);
        Assert.Equal("ON", address.State);
        Assert.Equal("M5H 2M9", address.PostalCode);
        Assert.Equal("CAN", address.Country);
        Assert.Equal(Address.AddressUse.Home, address.Use);
    }

    [Fact]
    public void Map_AdtA01_MapsTelecom()
    {
        var patient = _mapper.Map(LoadSample("ADT_A01.hl7")).Patient;

        var telecom = Assert.Single(patient.Telecom);
        Assert.Equal("(416)555-0123", telecom.Value);
        Assert.Equal(ContactPoint.ContactPointSystem.Phone, telecom.System);
        Assert.Equal(ContactPoint.ContactPointUse.Home, telecom.Use);
    }

    [Fact]
    public void Map_OruR01_ReturnsHeaderValuesAndPatient()
    {
        var result = _mapper.Map(LoadSample("ORU_R01.hl7"));

        Assert.Equal("MSG00002", result.MessageControlId);
        Assert.Equal("ORU^R01", result.MessageType);
        Assert.Equal("TGH", result.SendingFacility);
        Assert.Equal("MRN12345", result.Patient.Identifier[0].Value);
        Assert.Equal(2, result.Resources.Count);
    }

    [Fact]
    public void Map_OruR01_MapsObservationCodeAndValue()
    {
        var observation = Assert.Single(_mapper.Map(LoadSample("ORU_R01.hl7")).Observations);

        var coding = Assert.Single(observation.Code.Coding);
        Assert.Equal("http://loinc.org", coding.System);
        Assert.Equal("2345-7", coding.Code);
        Assert.Contains("Glucose", coding.Display);
        Assert.Equal(ObservationStatus.Final, observation.Status);

        var quantity = Assert.IsType<Quantity>(observation.Value);
        Assert.Equal(5.4m, quantity.Value);
        Assert.Equal("mmol/L", quantity.Unit);
        Assert.Equal("http://unitsofmeasure.org", quantity.System);
    }

    [Fact]
    public void Map_OruR01_MapsReferenceRangeAndInterpretation()
    {
        var observation = Assert.Single(_mapper.Map(LoadSample("ORU_R01.hl7")).Observations);

        var range = Assert.Single(observation.ReferenceRange);
        Assert.Equal(3.9m, range.Low!.Value);
        Assert.Equal(6.1m, range.High!.Value);
        Assert.Equal("mmol/L", range.Low!.Unit);

        var interpretation = Assert.Single(observation.Interpretation);
        Assert.Equal("N", interpretation.Coding[0].Code);
    }

    [Fact]
    public void Map_OruR01_ObservationReferencesPatient()
    {
        var result = _mapper.Map(LoadSample("ORU_R01.hl7"));
        var observation = Assert.Single(result.Observations);

        Assert.Equal($"Patient/{result.Patient.Id}", observation.Subject!.Reference);
        Assert.Equal("MRN12345", observation.Subject!.Identifier!.Value);
    }

    [Fact]
    public void Map_AcceptsNewlineSegmentSeparators()
    {
        var message = LoadSample("ADT_A01.hl7").Replace("\r", "\r\n");

        var result = _mapper.Map(message);

        Assert.Equal("MSG00001", result.MessageControlId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not an HL7 message")]
    [InlineData("PID|1||MRN12345^^^TGH^MR||Doe^Jane")]
    public void Map_UnparseableMessage_Throws(string message)
    {
        var ex = Assert.Throws<Hl7ValidationException>(() => _mapper.Map(message));

        Assert.Contains("HL7 message", ex.Message);
    }

    [Fact]
    public void Map_MissingMessageControlId_Throws()
    {
        var message = LoadSample("ADT_A01.hl7").Replace("|MSG00001|", "||");

        var ex = Assert.Throws<Hl7ValidationException>(() => _mapper.Map(message));

        Assert.Contains("MSH-10", ex.Message);
    }

    [Theory]
    [InlineData("ADT_A01.hl7")]
    [InlineData("ORU_R01.hl7")]
    public void Map_MissingPatientIdentifier_Throws(string sample)
    {
        var message = LoadSample(sample).Replace("MRN12345^^^TGH^MR", "");

        var ex = Assert.Throws<Hl7ValidationException>(() => _mapper.Map(message));

        Assert.Contains("PID-3", ex.Message);
    }

    [Theory]
    [InlineData("19881345")]
    [InlineData("1988051")]
    [InlineData("notadate")]
    public void Map_InvalidBirthDate_Throws(string birthDate)
    {
        var message = LoadSample("ADT_A01.hl7").Replace("|19880514|", $"|{birthDate}|");

        var ex = Assert.Throws<Hl7ValidationException>(() => _mapper.Map(message));

        Assert.Contains("PID-7", ex.Message);
    }

    [Fact]
    public void Map_UnsupportedMessageType_Throws()
    {
        var message = LoadSample("ADT_A01.hl7")
            .Replace("ADT^A01^ADT_A01", "ADT^A08^ADT_A01")
            .Replace("EVN|A01|", "EVN|A08|");

        var ex = Assert.Throws<Hl7ValidationException>(() => _mapper.Map(message));

        Assert.Contains("Unsupported message type 'ADT^A08'", ex.Message);
    }
}
