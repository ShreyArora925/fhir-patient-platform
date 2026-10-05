using System.Net;
using Fhir.Domain.Auditing;
using Fhir.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fhir.IntegrationTests;

public sealed class IngestTests(FhirApiFactory factory) : IClassFixture<FhirApiFactory>
{
    private HttpClient CreateIngestClient(string? apiKey)
    {
        var client = factory.CreateClient();
        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        return client;
    }

    [Fact]
    public async Task Ingest_WithoutApiKey_Returns401()
    {
        var response = await CreateIngestClient(null)
            .PostAsync("/hl7/messages", FhirApiFactory.Hl7Content(FhirApiFactory.LoadSample("ORU_R01.hl7")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_WithUnknownApiKey_Returns401()
    {
        var response = await CreateIngestClient("not-a-real-key")
            .PostAsync("/hl7/messages", FhirApiFactory.Hl7Content(FhirApiFactory.LoadSample("ORU_R01.hl7")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ingest_TghKeyWithNyghMessage_Returns403AndAuditsDenied()
    {
        var message = FhirApiFactory.LoadSample("ADT_A01_NYGH.hl7").Replace("|NYG00001|", "|NYG-FORBIDDEN|");

        var response = await CreateIngestClient(FhirApiFactory.TghApiKey)
            .PostAsync("/hl7/messages", FhirApiFactory.Hl7Content(message));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var db = factory.CreateSystemContext();
        var auditEvent = await db.AuditEvents.SingleAsync(a => a.ResourceId == "NYG-FORBIDDEN");
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("system:TGH", auditEvent.UserId);
    }

    [Fact]
    public async Task Ingest_MessageWithoutMsh_Returns400()
    {
        var response = await CreateIngestClient(FhirApiFactory.TghApiKey)
            .PostAsync("/hl7/messages", FhirApiFactory.Hl7Content("PID|1||MRN12345^^^TGH^MR"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("MSH", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ingest_InvalidBirthDate_Returns422InDirectMode()
    {
        var response = await CreateIngestClient(FhirApiFactory.TghApiKey)
            .PostAsync("/hl7/messages", FhirApiFactory.Hl7Content(FhirApiFactory.LoadSample("ADT_A01_BAD_DOB.hl7")));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("PID-7", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ingest_ValidMessage_Returns202ThenDuplicateReturns200()
    {
        var client = CreateIngestClient(FhirApiFactory.TghApiKey);
        var message = FhirApiFactory.LoadSample("ORU_R01.hl7").Replace("|MSG00002|", "|MSG-INGEST-TEST|");

        var first = await client.PostAsync("/hl7/messages", FhirApiFactory.Hl7Content(message));
        var second = await client.PostAsync("/hl7/messages", FhirApiFactory.Hl7Content(message));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("duplicate ignored", await second.Content.ReadAsStringAsync());

        await using var db = factory.CreateSystemContext();
        var observation = await db.Observations.SingleAsync(o => o.FhirJson.Contains(factory.TghPatientId.ToString()));
        Assert.Equal(factory.TghPatientId, observation.PatientId);
    }
}
