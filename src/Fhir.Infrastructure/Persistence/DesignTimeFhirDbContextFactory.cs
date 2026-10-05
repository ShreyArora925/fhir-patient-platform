using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fhir.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` to create SQL Server migrations.</summary>
public sealed class DesignTimeFhirDbContextFactory : IDesignTimeDbContextFactory<FhirDbContext>
{
    public FhirDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FhirDbContext>()
            .UseSqlServer(DependencyInjection.DefaultLocalDbConnectionString)
            .Options;

        return new FhirDbContext(options, SystemDataScope.Instance);
    }
}
