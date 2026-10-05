using System.Globalization;
using Fhir.Application.Hl7;
using Fhir.Domain.Messaging;
using Fhir.Domain.Patients;
using Fhir.Infrastructure.Persistence;
using Fhir.Infrastructure.Serialization;
using Hl7.Fhir.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fhir.Infrastructure.Hl7;

/// <summary>
/// Stores one HL7 message in a single transaction. Shared by the Worker (queue mode) and the API (direct mode).
/// </summary>
public sealed class Hl7MessageHandler(
    IFhirDbContextFactory contextFactory,
    IHl7ToFhirMapper mapper,
    TimeProvider timeProvider,
    ILogger<Hl7MessageHandler> logger) : IHl7MessageHandler
{
    private const string LoincSystem = "http://loinc.org";
    private const int MaxMrnLength = 64;

    public async Task<Hl7HandleResult> HandleAsync(string rawMessage, CancellationToken cancellationToken = default)
    {
        if (!Hl7Header.TryParse(rawMessage, out var header, out var error))
        {
            throw new Hl7ValidationException(error!);
        }

        if (header!.SendingFacility.Length == 0)
        {
            throw new Hl7ValidationException(
                $"MSH-4 (sending facility) is missing in message {header.MessageControlId}.");
        }

        // System scope: message processing has no request user and must see every hospital's patients.
        await using var db = contextFactory.CreateSystemContext();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 1. Duplicate check.
        var isDuplicate = await db.ProcessedMessages.AnyAsync(
            m => m.SendingFacility == header.SendingFacility && m.MessageControlId == header.MessageControlId,
            cancellationToken);

        if (isDuplicate)
        {
            logger.LogInformation(
                "HL7 message {MessageControlId} from {SendingFacility} is a duplicate; skipped",
                header.MessageControlId,
                header.SendingFacility);
            return new Hl7HandleResult(Hl7HandleOutcome.Duplicate, header.SendingFacility, header.MessageControlId);
        }

        // 2. Map. Hl7ValidationException propagates to the caller.
        var mapped = mapper.Map(rawMessage);
        var isAdt = mapped.MessageType.StartsWith("ADT^", StringComparison.Ordinal);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 3. Upsert the patient by (HospitalCode, MRN).
        var mrn = SelectMrn(mapped.Patient, header.MessageControlId);
        var patient = await db.Patients.SingleOrDefaultAsync(
            p => p.HospitalCode == header.SendingFacility && p.Mrn == mrn,
            cancellationToken);

        if (patient is null)
        {
            patient = new PatientRecord
            {
                Id = Guid.NewGuid(),
                HospitalCode = header.SendingFacility,
                Mrn = mrn,
                FhirJson = string.Empty,
                CreatedUtc = now,
            };
            ApplyDemographics(patient, mapped.Patient, now);
            db.Patients.Add(patient);
        }
        else if (isAdt)
        {
            ApplyDemographics(patient, mapped.Patient, now);
        }

        // 4. Observations, linked to the stored patient.
        var observationIds = new List<Guid>();
        foreach (var observation in mapped.Observations)
        {
            var record = CreateObservation(observation, patient, now);
            db.Observations.Add(record);
            observationIds.Add(record.Id);
        }

        // 5. Processed-message record, then commit.
        db.ProcessedMessages.Add(new ProcessedMessage
        {
            SendingFacility = header.SendingFacility,
            MessageControlId = header.MessageControlId,
            MessageType = mapped.MessageType,
            ReceivedUtc = now,
            RawHl7 = rawMessage,
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "HL7 message {MessageControlId} ({MessageType}) from {SendingFacility} stored for patient {PatientId} with {ObservationCount} observation(s)",
            header.MessageControlId,
            mapped.MessageType,
            header.SendingFacility,
            patient.Id,
            observationIds.Count);

        return new Hl7HandleResult(
            Hl7HandleOutcome.Processed,
            header.SendingFacility,
            header.MessageControlId,
            patient.Id,
            observationIds);
    }

    private static string SelectMrn(Patient patient, string messageControlId)
    {
        var identifier = patient.Identifier.FirstOrDefault(i => i.Type?.Coding.Any(c => c.Code == "MR") == true)
            ?? patient.Identifier.FirstOrDefault();

        var mrn = identifier?.Value?.Trim();
        if (string.IsNullOrEmpty(mrn))
        {
            throw new Hl7ValidationException($"PID-3 (patient identifier) is missing in message {messageControlId}.");
        }

        if (mrn.Length > MaxMrnLength)
        {
            throw new Hl7ValidationException(
                $"PID-3 (patient identifier) is longer than {MaxMrnLength} characters in message {messageControlId}.");
        }

        return mrn;
    }

    private static void ApplyDemographics(PatientRecord record, Patient fhirPatient, DateTime now)
    {
        fhirPatient.Id = record.Id.ToString();

        var name = fhirPatient.Name.FirstOrDefault();
        record.FamilyName = name?.Family;
        record.GivenNames = name is null ? null : string.Join(' ', name.Given);
        record.BirthDate = DateOnly.TryParseExact(
            fhirPatient.BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birthDate)
            ? birthDate
            : null;
        record.FhirJson = FhirJson.Serialize(fhirPatient);
        record.UpdatedUtc = now;
    }

    private static ObservationRecord CreateObservation(Observation observation, PatientRecord patient, DateTime now)
    {
        var id = Guid.NewGuid();
        observation.Id = id.ToString();
        observation.Subject = new ResourceReference($"Patient/{patient.Id}")
        {
            Identifier = observation.Subject?.Identifier,
        };

        var coding = observation.Code?.Coding.FirstOrDefault(c => c.System == LoincSystem)
            ?? observation.Code?.Coding.FirstOrDefault();
        var quantity = observation.Value as Quantity;

        return new ObservationRecord
        {
            Id = id,
            PatientId = patient.Id,
            HospitalCode = patient.HospitalCode,
            LoincCode = coding?.Code,
            Display = coding?.Display ?? observation.Code?.Text,
            Value = quantity?.Value,
            Unit = quantity?.Unit,
            EffectiveDate = (observation.Effective as FhirDateTime)?.ToDateTimeOffset(TimeSpan.Zero).UtcDateTime,
            FhirJson = FhirJson.Serialize(observation),
            CreatedUtc = now,
        };
    }
}
