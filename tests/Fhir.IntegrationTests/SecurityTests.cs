using System.Net;
using Fhir.Domain.Auditing;
using Fhir.Infrastructure.Serialization;
using Fhir.IntegrationTests.Infrastructure;
using Hl7.Fhir.Model;
using Microsoft.EntityFrameworkCore;

namespace Fhir.IntegrationTests;

public sealed class SecurityTests(FhirApiFactory factory) : IClassFixture<FhirApiFactory>
{
    [Fact]
    public async System.Threading.Tasks.Task GetPatients_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/Patient");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPatients_ClinicianWithoutHospitalClaim_Returns403()
    {
        var client = factory.CreateUserClient("dr.nohospital", "Clinician", hospital: null);

        var response = await client.GetAsync("/Patient");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPatients_ClinicianAtTgh_SeesOnlyTghPatients()
    {
        var client = factory.CreateUserClient("dr.tgh", "Clinician", "TGH");

        var response = await client.GetAsync("/Patient");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/fhir+json", response.Content.Headers.ContentType?.MediaType);

        var bundle = FhirJson.Deserialize<Bundle>(await response.Content.ReadAsStringAsync());
        Assert.Equal(Bundle.BundleType.Searchset, bundle.Type);
        var patient = Assert.IsType<Patient>(Assert.Single(bundle.Entry).Resource);
        Assert.Equal(factory.TghPatientId.ToString(), patient.Id);
        Assert.Equal("MRN12345", patient.Identifier[0].Value);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPatient_ClinicianAtTgh_ReadsOwnHospitalPatient()
    {
        var client = factory.CreateUserClient("dr.tgh", "Clinician", "TGH");

        var response = await client.GetAsync($"/Patient/{factory.TghPatientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var patient = FhirJson.Deserialize<Patient>(await response.Content.ReadAsStringAsync());
        Assert.Equal("Doe", patient.Name[0].Family);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetPatient_ClinicianAtTghReadingNyghPatient_Returns404AndAuditsDenied()
    {
        var client = factory.CreateUserClient("dr.tgh.denied", "Clinician", "TGH");

        var response = await client.GetAsync($"/Patient/{factory.NyghPatientId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var db = factory.CreateSystemContext();
        var auditEvent = await db.AuditEvents.SingleAsync(a =>
            a.UserId == "dr.tgh.denied" && a.Action == AuditActions.PatientRead);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal(factory.NyghPatientId.ToString(), auditEvent.ResourceId);
        Assert.Equal("TGH", auditEvent.HospitalCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task GetObservations_ClinicianAtTghForNyghPatient_ReturnsEmptyBundle()
    {
        var client = factory.CreateUserClient("dr.tgh", "Clinician", "TGH");

        var response = await client.GetAsync($"/Observation?patient={factory.NyghPatientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bundle = FhirJson.Deserialize<Bundle>(await response.Content.ReadAsStringAsync());
        Assert.Empty(bundle.Entry);
    }

    [Fact]
    public async System.Threading.Tasks.Task AdminAudit_Clinician_Returns403()
    {
        var client = factory.CreateUserClient("dr.tgh", "Clinician", "TGH");

        var response = await client.GetAsync("/admin/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async System.Threading.Tasks.Task AdminAudit_Admin_Returns200()
    {
        var client = factory.CreateUserClient("admin", "Admin", "TGH");

        var response = await client.GetAsync($"/admin/audit?patientId={factory.TghPatientId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
