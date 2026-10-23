using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    actor_ip = table.Column<IPAddress>(type: "inet", nullable: true),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    entity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_record",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "entity", "entity_id", "occurred_at" },
                descending: new[] { false, false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_entries_tenant_time",
                schema: "audit",
                table: "entries",
                columns: new[] { "tenant_id", "occurred_at" },
                descending: new[] { false, true });

            // Append-only: the trail is evidence, so the database refuses to change or remove what it recorded.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit.refuse_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit.entries is append-only';
                END
                $$;
                CREATE TRIGGER entries_append_only BEFORE UPDATE OR DELETE OR TRUNCATE ON audit.entries
                    FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER entries_append_only ON audit.entries; DROP FUNCTION audit.refuse_change();");

            migrationBuilder.DropTable(
                name: "entries",
                schema: "audit");
        }
    }
}
