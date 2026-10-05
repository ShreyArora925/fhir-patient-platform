using Fhir.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Fhir.UnitTests.Persistence;

/// <summary>An in-memory SQLite database that lives as long as this object.</summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SqliteTestDatabase()
    {
        _connection.Open();
        Options = new DbContextOptionsBuilder<FhirDbContext>().UseSqlite(_connection).Options;
        Factory = new FhirDbContextFactory(Options);

        using var db = CreateSystemContext();
        db.Database.EnsureCreated();
    }

    public DbContextOptions<FhirDbContext> Options { get; }

    public IFhirDbContextFactory Factory { get; }

    public FhirDbContext CreateSystemContext() => Factory.CreateSystemContext();

    public void Dispose() => _connection.Dispose();
}
