using System.Text.Json;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;

namespace Fhir.Infrastructure.Serialization;

/// <summary>
/// FHIR R4 JSON serialization using the Firely SDK's System.Text.Json converter.
/// </summary>
public static class FhirJson
{
    public const string ContentType = "application/fhir+json";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions().ForFhir(ModelInfo.ModelInspector);

    public static string Serialize(Resource resource) =>
        JsonSerializer.Serialize(resource, resource.GetType(), Options);

    public static T Deserialize<T>(string json)
        where T : Resource =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidOperationException($"Stored FHIR JSON could not be read as {typeof(T).Name}.");
}
