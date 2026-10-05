using Azure.Messaging.ServiceBus;
using Fhir.Application.Hl7;
using Fhir.Infrastructure.Messaging;

namespace Fhir.Worker;

/// <summary>
/// Receives HL7 messages from hl7-inbound and stores them with IHl7MessageHandler.
/// Success or duplicate: complete. Hl7ValidationException: dead-letter, no retry. Anything else: leave unsettled
/// so the lock expires and Service Bus redelivers; after MaxDeliveryCount (5) it dead-letters automatically.
/// </summary>
public sealed class Hl7QueueProcessor(
    ServiceBusClient client,
    ServiceBusQueueOptions queueOptions,
    IServiceScopeFactory scopeFactory,
    ILogger<Hl7QueueProcessor> logger) : IHostedService, IAsyncDisposable
{
    public const string ValidationFailedReason = "ValidationFailed";

    // Service Bus limits dead-letter descriptions to 4096 characters.
    private const int MaxDeadLetterDescriptionLength = 4096;

    private ServiceBusProcessor? _processor;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _processor = client.CreateProcessor(queueOptions.QueueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 4,
        });
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(cancellationToken);
        logger.LogInformation("Listening on Service Bus queue {QueueName}", queueOptions.QueueName);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync();
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["MessageControlId"] = message.MessageId,
            ["DeliveryCount"] = message.DeliveryCount,
        });

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<IHl7MessageHandler>();

            var result = await handler.HandleAsync(message.Body.ToString(), args.CancellationToken);
            await args.CompleteMessageAsync(message, args.CancellationToken);

            if (result.Outcome == Hl7HandleOutcome.Duplicate)
            {
                logger.LogInformation("HL7 message {MessageControlId} duplicate skipped", message.MessageId);
            }
            else
            {
                logger.LogInformation(
                    "HL7 message {MessageControlId} processed for patient {PatientId}",
                    message.MessageId,
                    result.PatientId);
            }
        }
        catch (Hl7ValidationException ex)
        {
            var description = ex.Message.Length > MaxDeadLetterDescriptionLength
                ? ex.Message[..MaxDeadLetterDescriptionLength]
                : ex.Message;

            await args.DeadLetterMessageAsync(message, ValidationFailedReason, description, args.CancellationToken);
            logger.LogWarning(
                "HL7 message {MessageControlId} dead-lettered ({Reason}): {Description}",
                message.MessageId,
                ValidationFailedReason,
                description);
        }
        catch (Exception ex) when (!args.CancellationToken.IsCancellationRequested)
        {
            logger.LogError(
                ex,
                "HL7 message {MessageControlId} failed on delivery {DeliveryCount}; it will be redelivered after the lock expires",
                message.MessageId,
                message.DeliveryCount);
            throw;
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(
            args.Exception,
            "Service Bus error ({ErrorSource}) on {EntityPath}",
            args.ErrorSource,
            args.EntityPath);
        return Task.CompletedTask;
    }
}
