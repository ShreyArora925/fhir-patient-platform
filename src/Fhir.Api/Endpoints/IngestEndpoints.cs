using Fhir.Api.Security;
using Fhir.Application.Auditing;
using Fhir.Application.Hl7;
using Fhir.Application.Security;
using Fhir.Domain.Auditing;

namespace Fhir.Api.Endpoints;

/// <summary>
/// HL7 v2 ingest for hospital systems (API key). Validates only what is needed to route the message, then hands it
/// to IHl7MessagePublisher; full parsing happens in the handler.
/// </summary>
public static class IngestEndpoints
{
    private const string ResourceType = "Hl7Message";

    public static IEndpointRouteBuilder MapIngestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/hl7/messages", IngestMessage)
            .RequireAuthorization(Policies.HospitalSystem)
            .Accepts<string>("text/plain")
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<IResult> IngestMessage(
        HttpRequest request,
        ICurrentUser currentUser,
        IHl7MessagePublisher publisher,
        IAuditLogger audit,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(IngestEndpoints));

        if (request.ContentType is { } contentType
            && !contentType.StartsWith("text/plain", StringComparison.OrdinalIgnoreCase))
        {
            await audit.LogAsync(AuditActions.MessageIngested, ResourceType, null, AuditOutcome.Failed, cancellationToken);
            return Results.Problem("Content-Type must be text/plain.", statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        using var reader = new StreamReader(request.Body);
        var rawMessage = await reader.ReadToEndAsync(cancellationToken);

        if (!Hl7Header.TryParse(rawMessage, out var header, out var error))
        {
            logger.LogWarning("Rejected HL7 message from {HospitalCode}: {Reason}", currentUser.HospitalCode, error);
            await audit.LogAsync(AuditActions.MessageIngested, ResourceType, null, AuditOutcome.Failed, cancellationToken);
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var controlId = header!.MessageControlId;

        if (!string.Equals(header.SendingFacility, currentUser.HospitalCode, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Rejected HL7 message {MessageControlId}: MSH-4 {SendingFacility} does not match API key hospital {HospitalCode}",
                controlId,
                header.SendingFacility,
                currentUser.HospitalCode);
            await audit.LogAsync(AuditActions.MessageIngested, ResourceType, controlId, AuditOutcome.Denied, cancellationToken);
            return Results.Problem(
                $"MSH-4 (sending facility) '{header.SendingFacility}' does not match the hospital for this API key.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        PublishResult result;
        try
        {
            result = await publisher.PublishAsync(rawMessage, header, cancellationToken);
        }
        catch (Hl7ValidationException ex)
        {
            // Only the development direct publisher maps in-process; with Service Bus this surfaces in the Worker.
            logger.LogWarning("HL7 message {MessageControlId} failed validation: {Reason}", controlId, ex.Message);
            await audit.LogAsync(AuditActions.MessageIngested, ResourceType, controlId, AuditOutcome.Failed, cancellationToken);
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Failed to publish HL7 message {MessageControlId}", controlId);
            await audit.LogAsync(AuditActions.MessageIngested, ResourceType, controlId, AuditOutcome.Failed, CancellationToken.None);
            return Results.Problem(
                "The message could not be accepted right now. Retry later.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        await audit.LogAsync(AuditActions.MessageIngested, ResourceType, controlId, AuditOutcome.Success, cancellationToken);

        return result.Outcome switch
        {
            PublishOutcome.Duplicate => Results.Ok(new IngestResponse(controlId, "duplicate ignored")),
            PublishOutcome.Processed => Results.Accepted(value: new IngestResponse(controlId, "processed")),
            _ => Results.Accepted(value: new IngestResponse(controlId, "queued")),
        };
    }

    public sealed record IngestResponse(string MessageControlId, string Status);
}
