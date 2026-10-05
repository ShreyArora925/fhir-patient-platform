using Fhir.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Fhir.UnitTests.Persistence;

public sealed class AuditAppendOnlyTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private async Task<long> InsertEventAsync()
    {
        await using var db = _database.CreateSystemContext();
        var auditEvent = new AuditEvent
        {
            TimestampUtc = DateTime.UtcNow,
            UserId = "dr.tgh",
            HospitalCode = "TGH",
            Action = AuditActions.PatientRead,
            ResourceType = "Patient",
            ResourceId = Guid.NewGuid().ToString(),
            Outcome = AuditOutcome.Success,
        };
        db.AuditEvents.Add(auditEvent);
        await db.SaveChangesAsync();
        return auditEvent.Id;
    }

    [Fact]
    public async Task Insert_IsAllowed()
    {
        var id = await InsertEventAsync();

        await using var db = _database.CreateSystemContext();
        Assert.True(await db.AuditEvents.AnyAsync(a => a.Id == id));
    }

    [Fact]
    public async Task Update_Throws()
    {
        var id = await InsertEventAsync();
        await using var db = _database.CreateSystemContext();
        var auditEvent = await db.AuditEvents.SingleAsync(a => a.Id == id);

        auditEvent.Outcome = AuditOutcome.Denied;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("append-only", ex.Message);
    }

    [Fact]
    public async Task Delete_Throws()
    {
        var id = await InsertEventAsync();
        await using var db = _database.CreateSystemContext();
        var auditEvent = await db.AuditEvents.SingleAsync(a => a.Id == id);

        db.AuditEvents.Remove(auditEvent);

        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }
}
