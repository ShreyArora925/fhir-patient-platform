namespace Fhir.Domain.Patients;

public class ObservationRecord
{
    public Guid Id { get; set; }

    public Guid PatientId { get; set; }

    public PatientRecord? Patient { get; set; }

    /// <summary>Copied from the patient so hospital isolation can filter observations directly.</summary>
    public required string HospitalCode { get; set; }

    public string? LoincCode { get; set; }

    public string? Display { get; set; }

    public decimal? Value { get; set; }

    public string? Unit { get; set; }

    public DateTime? EffectiveDate { get; set; }

    /// <summary>The FHIR R4 Observation resource, with subject = Patient/{PatientId}.</summary>
    public required string FhirJson { get; set; }

    public DateTime CreatedUtc { get; set; }
}
