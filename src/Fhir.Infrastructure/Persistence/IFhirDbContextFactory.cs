using Microsoft.EntityFrameworkCore;

namespace Fhir.Infrastructure.Persistence;

/// <summary>
/// Creates contexts that bypass hospital isolation. Only for HL7 processing and migrations, which have no
/// request user. Never use this to serve user data.
/// </summary>
public interface IFhirDbContextFactory
{
    FhirDbContext CreateSystemContext();
}

public sealed class FhirDbContextFactory(DbContextOptions<FhirDbContext> options) : IFhirDbContextFactory
{
    public FhirDbContext CreateSystemContext() => new(options, SystemDataScope.Instance);
}
