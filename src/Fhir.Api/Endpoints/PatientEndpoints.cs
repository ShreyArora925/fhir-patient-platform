using Fhir.Api.Security;
using Fhir.Application.Auditing;
using Fhir.Domain.Auditing;
using Fhir.Infrastructure.Persistence;
using Fhir.Infrastructure.Serialization;
using Hl7.Fhir.Model;
using Microsoft.EntityFrameworkCore;

namespace Fhir.Api.Endpoints;

/// <summary>
/// FHIR read endpoints. FhirDbContext's global query filters limit every query to the caller's hospital.
/// </summary>
public static class PatientEndpoints
{
    private const string PatientType = "Patient";
    private const string ObservationType = "Observation";

    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/Patient", SearchPatients).RequireAuthorization(Policies.CanReadPatients);
        app.MapGet("/Patient/{id}", ReadPatient).RequireAuthorization(Policies.CanReadPatients);
        app.MapGet("/Observation", SearchObservations).RequireAuthorization(Policies.CanReadPatients);
        return app;
    }

    private static async Task<IResult> SearchPatients(
        HttpRequest request,
        FhirDbContext db,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        var records = await db.Patients
            .AsNoTracking()
            .OrderBy(p => p.FamilyName)
            .ThenBy(p => p.GivenNames)
            .Select(p => p.FhirJson)
            .ToListAsync(cancellationToken);

        await audit.LogAsync(AuditActions.PatientSearch, PatientType, null, AuditOutcome.Success, cancellationToken);

        return FhirResults.SearchSet(request, records.Select(FhirJson.Deserialize<Patient>).ToList());
    }

    private static async Task<IResult> ReadPatient(
        string id,
        FhirDbContext db,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var patientId))
        {
            await audit.LogAsync(AuditActions.PatientRead, PatientType, id, AuditOutcome.Failed, cancellationToken);
            return FhirResults.NotFound(PatientType, id);
        }

        var fhirJson = await db.Patients
            .AsNoTracking()
            .Where(p => p.Id == patientId)
            .Select(p => p.FhirJson)
            .SingleOrDefaultAsync(cancellationToken);

        if (fhirJson is not null)
        {
            await audit.LogAsync(AuditActions.PatientRead, PatientType, patientId.ToString(), AuditOutcome.Success, cancellationToken);
            return FhirResults.Resource(FhirJson.Deserialize<Patient>(fhirJson));
        }

        // Same 404 either way so callers cannot probe other hospitals; the audit records which case it was.
        var outcome = await ExistsInAnotherHospitalAsync(db, patientId, cancellationToken)
            ? AuditOutcome.Denied
            : AuditOutcome.Failed;
        await audit.LogAsync(AuditActions.PatientRead, PatientType, patientId.ToString(), outcome, cancellationToken);

        return FhirResults.NotFound(PatientType, id);
    }

    private static async Task<IResult> SearchObservations(
        string? patient,
        HttpRequest request,
        FhirDbContext db,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        // Accept both "patient={id}" and "patient=Patient/{id}".
        var reference = patient?.Trim() ?? string.Empty;
        if (reference.StartsWith("Patient/", StringComparison.Ordinal))
        {
            reference = reference["Patient/".Length..];
        }

        if (!Guid.TryParse(reference, out var patientId))
        {
            await audit.LogAsync(AuditActions.ObservationSearch, ObservationType, patient, AuditOutcome.Failed, cancellationToken);
            return FhirResults.BadRequest("The 'patient' search parameter is required and must be a Patient id.");
        }

        var resourceId = patientId.ToString();
        var isVisible = await db.Patients.AnyAsync(p => p.Id == patientId, cancellationToken);
        if (!isVisible)
        {
            // An empty searchset either way; only the audit distinguishes another hospital's patient.
            var outcome = await ExistsInAnotherHospitalAsync(db, patientId, cancellationToken)
                ? AuditOutcome.Denied
                : AuditOutcome.Failed;
            await audit.LogAsync(AuditActions.ObservationSearch, ObservationType, resourceId, outcome, cancellationToken);
            return FhirResults.SearchSet(request, []);
        }

        var records = await db.Observations
            .AsNoTracking()
            .Where(o => o.PatientId == patientId)
            .OrderByDescending(o => o.EffectiveDate)
            .ThenByDescending(o => o.CreatedUtc)
            .Select(o => o.FhirJson)
            .ToListAsync(cancellationToken);

        await audit.LogAsync(AuditActions.ObservationSearch, ObservationType, resourceId, AuditOutcome.Success, cancellationToken);

        return FhirResults.SearchSet(request, records.Select(FhirJson.Deserialize<Observation>).ToList());
    }

    /// <summary>The only place hospital isolation is bypassed: an existence check whose result is never returned.</summary>
    private static Task<bool> ExistsInAnotherHospitalAsync(FhirDbContext db, Guid patientId, CancellationToken cancellationToken) =>
        db.Patients.IgnoreQueryFilters().AnyAsync(p => p.Id == patientId, cancellationToken);
}
