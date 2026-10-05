using Azure.Messaging.ServiceBus;
using Fhir.Application.Auditing;
using Fhir.Application.Hl7;
using Fhir.Infrastructure.Auditing;
using Fhir.Infrastructure.Hl7;
using Fhir.Infrastructure.Messaging;
using Fhir.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Fhir.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "FhirDb";
    public const string ServiceBusConnectionStringKey = "ServiceBus:ConnectionString";
    public const string DefaultQueueName = "hl7-inbound";

    public const string DefaultLocalDbConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=FhirPlatform;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>
    /// Database, HL7 mapping and message handling. Configuration:
    /// ConnectionStrings:FhirDb, and Database:Provider = SqlServer (default) or Sqlite.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Options are singletons so the system context factory (also a singleton) can use them.
        services.AddDbContext<FhirDbContext>(
            (sp, options) => ConfigureDatabase(options, sp.GetRequiredService<IConfiguration>()),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);

        // Request-scoped contexts see only the current user's hospital. ICurrentUser must be registered by the host;
        // without it, resolving FhirDbContext fails rather than silently showing every hospital.
        services.AddScoped<IDataScope, CurrentUserDataScope>();
        services.AddSingleton<IFhirDbContextFactory, FhirDbContextFactory>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IHl7ToFhirMapper, Hl7ToFhirMapper>();
        services.AddScoped<IHl7MessageHandler, Hl7MessageHandler>();
        services.AddScoped<IAuditLogger, AuditLogger>();

        return services;
    }

    /// <summary>
    /// Registers the IHl7MessagePublisher: Service Bus when ServiceBus:ConnectionString is set, otherwise the
    /// in-process direct publisher, which is only allowed in Development.
    /// </summary>
    /// <returns>True if Service Bus is used; false for direct mode.</returns>
    public static bool AddHl7Publishing(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (services.AddServiceBusClient(configuration))
        {
            services.AddSingleton<IHl7MessagePublisher, ServiceBusHl7MessagePublisher>();
            return true;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{ServiceBusConnectionStringKey} must be configured outside Development. "
                + "The in-process direct publisher is for local development only.");
        }

        services.AddScoped<IHl7MessagePublisher, DirectHl7MessagePublisher>();
        services.AddHostedService<DirectModeStartupWarning>();
        return false;
    }

    /// <summary>Registers ServiceBusClient and queue options if a connection string is configured.</summary>
    public static bool AddServiceBusClient(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration[ServiceBusConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        services.TryAddSingleton(_ => new ServiceBusClient(connectionString));
        services.TryAddSingleton(new ServiceBusQueueOptions(configuration["ServiceBus:QueueName"] ?? DefaultQueueName));
        return true;
    }

    /// <summary>Applies migrations (SQL Server) or creates the schema (SQLite). Call in Development only.</summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var db = services.GetRequiredService<IFhirDbContextFactory>().CreateSystemContext();

        if (db.Database.IsSqlServer())
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
    }

    private static void ConfigureDatabase(DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} is not configured.");

        var provider = configuration["Database:Provider"] ?? "SqlServer";
        if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlite(connectionString);
        }
        else
        {
            options.UseSqlServer(connectionString);
        }
    }
}
