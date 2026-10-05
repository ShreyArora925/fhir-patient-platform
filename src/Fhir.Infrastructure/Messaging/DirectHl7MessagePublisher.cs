using Fhir.Application.Hl7;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fhir.Infrastructure.Messaging;

/// <summary>
/// DEVELOPMENT ONLY. Processes messages in-process instead of queueing them, so there is no durability:
/// a crash between acceptance and storage loses the message. Refuses to run outside Development.
/// </summary>
public sealed class DirectHl7MessagePublisher : IHl7MessagePublisher
{
    private readonly IHl7MessageHandler _handler;

    public DirectHl7MessagePublisher(IHl7MessageHandler handler, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"{nameof(DirectHl7MessagePublisher)} is for Development only. Configure ServiceBus:ConnectionString.");
        }

        _handler = handler;
    }

    public async Task<PublishResult> PublishAsync(
        string rawMessage,
        Hl7Header header,
        CancellationToken cancellationToken = default)
    {
        var result = await _handler.HandleAsync(rawMessage, cancellationToken);

        return new PublishResult(result.Outcome == Hl7HandleOutcome.Duplicate
            ? PublishOutcome.Duplicate
            : PublishOutcome.Processed);
    }
}

/// <summary>Logs at startup that direct mode is active, so nobody mistakes it for the queued pipeline.</summary>
internal sealed class DirectModeStartupWarning(ILogger<DirectHl7MessagePublisher> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "ServiceBus:ConnectionString is not configured: HL7 messages are processed in-process by "
            + "DirectHl7MessagePublisher. Queue durability and retries are DISABLED. Development only.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
