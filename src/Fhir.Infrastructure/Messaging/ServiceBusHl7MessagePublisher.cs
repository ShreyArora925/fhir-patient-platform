using Azure.Messaging.ServiceBus;
using Fhir.Application.Hl7;
using Microsoft.Extensions.Logging;

namespace Fhir.Infrastructure.Messaging;

/// <summary>
/// Sends accepted HL7 messages to the hl7-inbound queue for the Worker to process.
/// </summary>
public sealed class ServiceBusHl7MessagePublisher(
    ServiceBusClient client,
    ServiceBusQueueOptions queueOptions,
    ILogger<ServiceBusHl7MessagePublisher> logger) : IHl7MessagePublisher, IAsyncDisposable
{
    public const string Hl7ContentType = "x-application/hl7-v2+er7";

    private readonly ServiceBusSender _sender = client.CreateSender(queueOptions.QueueName);

    public async Task<PublishResult> PublishAsync(
        string rawMessage,
        Hl7Header header,
        CancellationToken cancellationToken = default)
    {
        var message = new ServiceBusMessage(rawMessage)
        {
            MessageId = header.MessageControlId,
            ContentType = Hl7ContentType,
            Subject = header.MessageType,
        };
        message.ApplicationProperties["SendingFacility"] = header.SendingFacility;

        await _sender.SendMessageAsync(message, cancellationToken);

        logger.LogInformation(
            "HL7 message {MessageControlId} from {SendingFacility} queued on {QueueName}",
            header.MessageControlId,
            header.SendingFacility,
            queueOptions.QueueName);

        return new PublishResult(PublishOutcome.Queued);
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}

public sealed record ServiceBusQueueOptions(string QueueName);
