using Fhir.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace Fhir.Api.Security;

public static class Policies
{
    /// <summary>Clinicians and admins with a hospital claim. Data is further limited to that hospital.</summary>
    public const string CanReadPatients = nameof(CanReadPatients);

    public const string AdminOnly = nameof(AdminOnly);

    /// <summary>Hospital systems authenticated by API key (ingest only).</summary>
    public const string HospitalSystem = nameof(HospitalSystem);

    public static AuthorizationBuilder AddFhirPolicies(this AuthorizationBuilder builder) =>
        builder
            .AddPolicy(CanReadPatients, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Clinician, Roles.Admin)
                .RequireClaim(ClaimNames.Hospital))
            .AddPolicy(AdminOnly, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Admin))
            .AddPolicy(HospitalSystem, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireRole(Roles.HospitalSystem)
                .RequireClaim(ClaimNames.Hospital));
}
