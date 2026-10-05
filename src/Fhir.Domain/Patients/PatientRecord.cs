namespace Fhir.Domain.Patients;

/// <summary>
/// A patient as known to one hospital. (HospitalCode, Mrn) is unique; the same MRN at two hospitals is two patients.
/// </summary>
public class PatientRecord
{
    public Guid Id { get; set; }

    public required string HospitalCode { get; set; }

    public required string Mrn { get; set; }

    public string? FamilyName { get; set; }

    /// <summary>Given names separated by a single space.</summary>
    public string? GivenNames { get; set; }

    public DateOnly? BirthDate { get; set; }

    /// <summary>The latest FHIR R4 Patient resource for this patient, with id = <see cref="Id"/>.</summary>
    public required string FhirJson { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public List<ObservationRecord> Observations { get; set; } = [];
}
