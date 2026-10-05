using Fhir.Application.Hl7;
using Fhir.Infrastructure.Hl7;
using Fhir.Infrastructure.Serialization;
using Fhir.UnitTests.Persistence;
using Hl7.Fhir.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fhir.UnitTests.Hl7;

public sealed class Hl7MessageHandlerTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly Hl7MessageHandler _handler;

    public Hl7MessageHandlerTests()
    {
        _handler = new Hl7MessageHandler(
            _database.Factory,
            new Hl7ToFhirMapper(),
            TimeProvider.System,
            NullLogger<Hl7MessageHandler>.Instance);
    }

    public void Dispose() => _database.Dispose();

    private static string LoadSample(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "hl7", fileName));

    [Fact]
    public async System.Threading.Tasks.Task Adt_CreatesPatient()
    {
        var result = await _handler.HandleAsync(LoadSample("ADT_A01.hl7"));

        Assert.Equal(Hl7HandleOutcome.Processed, result.Outcome);

        await using var db = _database.CreateSystemContext();
        var patient = await db.Patients.SingleAsync();
        Assert.Equal(result.PatientId, patient.Id);
        Assert.Equal("TGH", patient.HospitalCode);
        Assert.Equal("MRN12345", patient.Mrn);
        Assert.Equal("Doe", patient.FamilyName);
        Assert.Equal("Jane Marie", patient.GivenNames);
        Assert.Equal(new DateOnly(1988, 5, 14), patient.BirthDate);

        var fhirPatient = FhirJson.Deserialize<Patient>(patient.FhirJson);
        Assert.Equal(patient.Id.ToString(), fhirPatient.Id);

        var processed = await db.ProcessedMessages.SingleAsync();
        Assert.Equal(("TGH", "MSG00001", "ADT^A01"), (processed.SendingFacility, processed.MessageControlId, processed.MessageType));
    }

    [Fact]
    public async System.Threading.Tasks.Task SecondAdt_SameHospitalAndMrn_UpdatesPatientInsteadOfDuplicating()
    {
        var first = await _handler.HandleAsync(LoadSample("ADT_A01.hl7"));
        var updatedMessage = LoadSample("ADT_A01.hl7")
            .Replace("|MSG00001|", "|MSG00002|")
            .Replace("Doe^Jane^Marie", "Smith^Jane^Marie");

        var second = await _handler.HandleAsync(updatedMessage);

        Assert.Equal(Hl7HandleOutcome.Processed, second.Outcome);
        Assert.Equal(first.PatientId, second.PatientId);

        await using var db = _database.CreateSystemContext();
        var patient = await db.Patients.SingleAsync();
        Assert.Equal("Smith", patient.FamilyName);
        Assert.Equal("Smith", FhirJson.Deserialize<Patient>(patient.FhirJson).Name[0].Family);
        Assert.Equal(2, await db.ProcessedMessages.CountAsync());
    }

    [Fact]
    public async System.Threading.Tasks.Task SameMessageTwice_IsDetectedAsDuplicate()
    {
        await _handler.HandleAsync(LoadSample("ADT_A01.hl7"));

        var result = await _handler.HandleAsync(LoadSample("ADT_A01.hl7"));

        Assert.Equal(Hl7HandleOutcome.Duplicate, result.Outcome);
        Assert.Equal("MSG00001", result.MessageControlId);
        Assert.Null(result.PatientId);

        await using var db = _database.CreateSystemContext();
        Assert.Equal(1, await db.Patients.CountAsync());
        Assert.Equal(1, await db.ProcessedMessages.CountAsync());
    }

    [Fact]
    public async System.Threading.Tasks.Task Oru_ForExistingPatient_LinksObservationToSamePatientId()
    {
        var adt = await _handler.HandleAsync(LoadSample("ADT_A01.hl7"));

        var oru = await _handler.HandleAsync(LoadSample("ORU_R01.hl7"));

        Assert.Equal(adt.PatientId, oru.PatientId);

        await using var db = _database.CreateSystemContext();
        Assert.Equal(1, await db.Patients.CountAsync());

        var observation = await db.Observations.SingleAsync();
        Assert.Equal(adt.PatientId, observation.PatientId);
        Assert.Equal("TGH", observation.HospitalCode);
        Assert.Equal("2345-7", observation.LoincCode);
        Assert.Equal(5.4m, observation.Value);
        Assert.Equal("mmol/L", observation.Unit);

        var fhirObservation = FhirJson.Deserialize<Observation>(observation.FhirJson);
        Assert.Equal($"Patient/{adt.PatientId}", fhirObservation.Subject!.Reference);
        Assert.Equal(observation.Id.ToString(), fhirObservation.Id);
    }

    [Fact]
    public async System.Threading.Tasks.Task Oru_ForUnknownPatient_CreatesPatientFromPid()
    {
        var result = await _handler.HandleAsync(LoadSample("ORU_R01.hl7"));

        await using var db = _database.CreateSystemContext();
        var patient = await db.Patients.SingleAsync();
        Assert.Equal(result.PatientId, patient.Id);
        Assert.Equal("MRN12345", patient.Mrn);
        Assert.Equal("Doe", patient.FamilyName);
        Assert.Equal(patient.Id, (await db.Observations.SingleAsync()).PatientId);
    }

    [Fact]
    public async System.Threading.Tasks.Task SameMrnAtTwoHospitals_CreatesTwoPatients()
    {
        // Same MRN and even the same MSH-10, but a different sending facility: neither a duplicate nor the same patient.
        var nyghMessage = LoadSample("ADT_A01.hl7").Replace("|ADT1|TGH|", "|ADT1|NYGH|");

        var tgh = await _handler.HandleAsync(LoadSample("ADT_A01.hl7"));
        var nygh = await _handler.HandleAsync(nyghMessage);

        Assert.Equal(Hl7HandleOutcome.Processed, nygh.Outcome);
        Assert.NotEqual(tgh.PatientId, nygh.PatientId);

        await using var db = _database.CreateSystemContext();
        var patients = await db.Patients.OrderBy(p => p.HospitalCode).ToListAsync();
        Assert.Equal(["NYGH", "TGH"], patients.Select(p => p.HospitalCode));
        Assert.All(patients, p => Assert.Equal("MRN12345", p.Mrn));
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidationError_PropagatesAndStoresNothing()
    {
        var ex = await Assert.ThrowsAsync<Hl7ValidationException>(
            () => _handler.HandleAsync(LoadSample("ADT_A01_BAD_DOB.hl7")));

        Assert.Contains("PID-7", ex.Message);

        await using var db = _database.CreateSystemContext();
        Assert.Equal(0, await db.Patients.CountAsync());
        Assert.Equal(0, await db.ProcessedMessages.CountAsync());
    }

    [Fact]
    public async System.Threading.Tasks.Task MissingMessageControlId_ThrowsValidationException()
    {
        var message = LoadSample("ADT_A01.hl7").Replace("|MSG00001|", "||");

        var ex = await Assert.ThrowsAsync<Hl7ValidationException>(() => _handler.HandleAsync(message));

        Assert.Contains("MSH-10", ex.Message);
    }
}
