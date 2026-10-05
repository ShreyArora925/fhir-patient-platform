using Fhir.Application.Security;
using Fhir.Infrastructure;
using Fhir.Worker;

var builder = Host.CreateApplicationBuilder(args);

if (!builder.Services.AddServiceBusClient(builder.Configuration))
{
    // Nothing to listen to. Locally without Service Bus, the API's direct mode processes messages instead.
    using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
    loggerFactory.CreateLogger("Fhir.Worker").LogWarning(
        "{Key} is not configured; the Worker has nothing to process and will exit.",
        DependencyInjection.ServiceBusConnectionStringKey);
    return;
}

builder.Services.AddSingleton<ICurrentUser, WorkerCurrentUser>();
builder.Services.AddInfrastructure();
builder.Services.AddHostedService<Hl7QueueProcessor>();

var host = builder.Build();
host.Run();
