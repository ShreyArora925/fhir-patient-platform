using Fhir.Infrastructure.Serialization;
using Hl7.Fhir.Model;

namespace Fhir.Api.Endpoints;

/// <summary>Builds application/fhir+json responses with the Firely serializer.</summary>
internal static class FhirResults
{
    public static IResult Resource(Resource resource, int statusCode = StatusCodes.Status200OK) =>
        Results.Text(FhirJson.Serialize(resource), FhirJson.ContentType, statusCode: statusCode);

    public static IResult SearchSet(HttpRequest request, IReadOnlyCollection<Resource> resources)
    {
        var baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";

        var bundle = new Bundle
        {
            Id = Guid.NewGuid().ToString(),
            Type = Bundle.BundleType.Searchset,
            Total = resources.Count,
            Meta = new Meta { LastUpdated = DateTimeOffset.UtcNow },
        };
        bundle.Link.Add(new Bundle.LinkComponent
        {
            Relation = "self",
            Url = $"{baseUrl}{request.Path}{request.QueryString}",
        });

        foreach (var resource in resources)
        {
            bundle.Entry.Add(new Bundle.EntryComponent
            {
                FullUrl = $"{baseUrl}/{resource.TypeName}/{resource.Id}",
                Resource = resource,
                Search = new Bundle.SearchComponent { Mode = Bundle.SearchEntryMode.Match },
            });
        }

        return Resource(bundle);
    }

    public static IResult NotFound(string resourceType, string id) =>
        Outcome(OperationOutcome.IssueType.NotFound, $"{resourceType}/{id} was not found.", StatusCodes.Status404NotFound);

    public static IResult BadRequest(string message) =>
        Outcome(OperationOutcome.IssueType.Invalid, message, StatusCodes.Status400BadRequest);

    private static IResult Outcome(OperationOutcome.IssueType type, string message, int statusCode)
    {
        var outcome = new OperationOutcome();
        outcome.Issue.Add(new OperationOutcome.IssueComponent
        {
            Severity = OperationOutcome.IssueSeverity.Error,
            Code = type,
            Diagnostics = message,
        });

        return Resource(outcome, statusCode);
    }
}
