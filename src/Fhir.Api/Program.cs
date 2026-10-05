using System.Text.Json.Serialization;
using Fhir.Api.Endpoints;
using Fhir.Api.Security;
using Fhir.Application.Security;
using Fhir.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.AddInfrastructure();
builder.Services.AddHl7Publishing(builder.Configuration, builder.Environment);

builder.Services.Configure<IngestOptions>(builder.Configuration.GetSection(IngestOptions.SectionName));

// Users: JWT bearer, configured from Authentication:Schemes:Bearer (dotnet user-jwts locally, Entra ID in Azure).
// Hospital systems: X-Api-Key, used only by the ingest endpoint's policy.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer()
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

builder.Services.AddAuthorizationBuilder().AddFhirPolicies();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.InitializeDatabaseAsync();
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapIngestEndpoints();
app.MapPatientEndpoints();
app.MapAdminEndpoints();

app.Run();

/// <summary>Exposed for WebApplicationFactory in the integration tests.</summary>
public partial class Program;
