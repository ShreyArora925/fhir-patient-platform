using System.Net.Http.Headers;
using Fhir.Application.Hl7;
using Fhir.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fhir.IntegrationTests.Infrastructure;

/// <summary>
/// The real API on an in-memory SQLite database, in Development (direct publisher, no Service Bus), with the
/// test auth handler instead of JWT. Seeds one TGH patient (ADT_A01) and one NYGH patient (ADT_A01_NYGH).
/// </summary>
public sealed class FhirApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TghApiKey = "dev-tgh-ingest-key";

    private readonly string _connectionString = $"DataSource=file:fhir-tests-{Guid.NewGuid():N}?mode=memory&cache=shared";
    private readonly SqliteConnection _keepAlive;

    public FhirApiFactory()
    {
        // A shared-cache in-memory SQLite database lives only while at least one connection is open.
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    public Guid TghPatientId { get; private set; }

    public Guid NyghPatientId { get; private set; }

    public static string LoadSample(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "hl7", fileName));

    public async Task InitializeAsync()
    {
        TghPatientId = await SeedAsync("ADT_A01.hl7");
        NyghPatientId = await SeedAsync("ADT_A01_NYGH.hl7");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }

    public HttpClient CreateUserClient(string userId, string roles, string? hospital)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        if (hospital is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.HospitalHeader, hospital);
        }

        return client;
    }

    public static HttpContent Hl7Content(string message)
    {
        var content = new StringContent(message);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return content;
    }

    public FhirDbContext CreateSystemContext() =>
        Services.GetRequiredService<IFhirDbContextFactory>().CreateSystemContext();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:FhirDb"] = _connectionString,
        }));

        builder.ConfigureTestServices(services =>
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null));
    }

    private async Task<Guid> SeedAsync(string sample)
    {
        await using var scope = Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IHl7MessageHandler>();
        var result = await handler.HandleAsync(LoadSample(sample));
        return result.PatientId!.Value;
    }
}
