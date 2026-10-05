using Fhir.Api.Security;
using Fhir.Application.Auditing;
using Fhir.Domain.Auditing;
using Fhir.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fhir.Api.Endpoints;

public static class AdminEndpoints
{
    private const int MaxResults = 1000;

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/audit", SearchAudit).RequireAuthorization(Policies.AdminOnly);
        return app;
    }

    /// <summary>Audit trail, newest first. patientId matches events whose ResourceId is that patient.</summary>
    private static async Task<IResult> SearchAudit(
        Guid? patientId,
        DateTime? from,
        DateTime? to,
        FhirDbContext db,
        IAuditLogger audit,
        CancellationToken cancellationToken)
    {
        var query = db.AuditEvents.AsNoTracking();

        if (patientId is { } id)
        {
            var resourceId = id.ToString();
            query = query.Where(a => a.ResourceId == resourceId);
        }

        if (from is { } fromUtc)
        {
            fromUtc = AsUtc(fromUtc);
            query = query.Where(a => a.TimestampUtc >= fromUtc);
        }

        if (to is { } toUtc)
        {
            toUtc = AsUtc(toUtc);
            query = query.Where(a => a.TimestampUtc <= toUtc);
        }

        var events = await query
            .OrderByDescending(a => a.Id)
            .Take(MaxResults)
            .ToListAsync(cancellationToken);

        // Reading the audit trail is itself audited.
        await audit.LogAsync(AuditActions.AuditSearch, "AuditEvent", patientId?.ToString(), AuditOutcome.Success, cancellationToken);

        return Results.Ok(events);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
