using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fhir.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Layer 2 of the append-only audit guarantee: the database itself rejects UPDATE and DELETE on AuditEvents,
    /// even from code that bypasses FhirDbContext (layer 1).
    /// </summary>
    public partial class AuditAppendOnlyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                CREATE TRIGGER [dbo].[{FhirDbContext.AuditTriggerName}]
                ON [dbo].[AuditEvents]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'AuditEvents is append-only: UPDATE and DELETE are not allowed.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP TRIGGER IF EXISTS [dbo].[{FhirDbContext.AuditTriggerName}];");
        }
    }
}
