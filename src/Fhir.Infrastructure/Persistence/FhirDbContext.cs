using Fhir.Domain.Auditing;
using Fhir.Domain.Messaging;
using Fhir.Domain.Patients;
using Microsoft.EntityFrameworkCore;

namespace Fhir.Infrastructure.Persistence;

public class FhirDbContext(DbContextOptions<FhirDbContext> options, IDataScope dataScope) : DbContext(options)
{
    public const string AuditTriggerName = "TR_AuditEvents_AppendOnly";

    // Read by the global query filters; EF Core parameterizes them per context instance.
    private readonly bool _isSystemScope = dataScope.IsSystem;
    private readonly string? _hospitalCode = dataScope.HospitalCode;

    public DbSet<PatientRecord> Patients => Set<PatientRecord>();

    public DbSet<ObservationRecord> Observations => Set<ObservationRecord>();

    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureAuditIsAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PatientRecord>(patient =>
        {
            patient.ToTable("Patients");
            patient.HasKey(p => p.Id);
            patient.Property(p => p.Id).ValueGeneratedNever();
            patient.Property(p => p.HospitalCode).HasMaxLength(64).IsRequired();
            patient.Property(p => p.Mrn).HasMaxLength(64).IsRequired();
            patient.Property(p => p.FamilyName).HasMaxLength(200);
            patient.Property(p => p.GivenNames).HasMaxLength(200);
            patient.Property(p => p.FhirJson).IsRequired();
            patient.HasIndex(p => new { p.HospitalCode, p.Mrn }).IsUnique();
            patient.HasQueryFilter(p => _isSystemScope || p.HospitalCode == _hospitalCode);
        });

        modelBuilder.Entity<ObservationRecord>(observation =>
        {
            observation.ToTable("Observations");
            observation.HasKey(o => o.Id);
            observation.Property(o => o.Id).ValueGeneratedNever();
            observation.Property(o => o.HospitalCode).HasMaxLength(64).IsRequired();
            observation.Property(o => o.LoincCode).HasMaxLength(32);
            observation.Property(o => o.Display).HasMaxLength(256);
            observation.Property(o => o.Value).HasPrecision(18, 6);
            observation.Property(o => o.Unit).HasMaxLength(32);
            observation.Property(o => o.FhirJson).IsRequired();
            observation.HasIndex(o => o.PatientId);
            observation.HasOne(o => o.Patient)
                .WithMany(p => p.Observations)
                .HasForeignKey(o => o.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
            observation.HasQueryFilter(o => _isSystemScope || o.HospitalCode == _hospitalCode);
        });

        modelBuilder.Entity<ProcessedMessage>(message =>
        {
            message.ToTable("ProcessedMessages");
            message.HasKey(m => new { m.SendingFacility, m.MessageControlId });
            message.Property(m => m.SendingFacility).HasMaxLength(64);
            message.Property(m => m.MessageControlId).HasMaxLength(200);
            message.Property(m => m.MessageType).HasMaxLength(32).IsRequired();
            message.Property(m => m.RawHl7).IsRequired();
        });

        modelBuilder.Entity<AuditEvent>(audit =>
        {
            // The trigger blocks UPDATE and DELETE in the database (see the AuditAppendOnlyTrigger migration).
            // Declaring it stops EF Core using OUTPUT clauses, which SQL Server rejects on tables with triggers.
            audit.ToTable("AuditEvents", table => table.HasTrigger(AuditTriggerName));
            audit.HasKey(a => a.Id);
            audit.Property(a => a.Id).ValueGeneratedOnAdd();
            audit.Property(a => a.UserId).HasMaxLength(256).IsRequired();
            audit.Property(a => a.DisplayName).HasMaxLength(256);
            audit.Property(a => a.HospitalCode).HasMaxLength(64);
            audit.Property(a => a.Action).HasMaxLength(64).IsRequired();
            audit.Property(a => a.ResourceType).HasMaxLength(64).IsRequired();
            audit.Property(a => a.ResourceId).HasMaxLength(200);
            audit.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(16);
            audit.Property(a => a.IpAddress).HasMaxLength(64);
            audit.Property(a => a.CorrelationId).HasMaxLength(128);
            audit.HasIndex(a => a.TimestampUtc);
            audit.HasIndex(a => a.ResourceId);
        });
    }

    /// <summary>Layer 1 of the append-only guarantee; the database trigger is layer 2.</summary>
    private void EnsureAuditIsAppendOnly()
    {
        var changed = ChangeTracker.Entries<AuditEvent>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);

        if (changed)
        {
            throw new InvalidOperationException("Audit events are append-only and cannot be modified or deleted.");
        }
    }
}
