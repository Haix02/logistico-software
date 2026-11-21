using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotent_requests",
                schema: "iam",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    principal = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    key = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    location = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    e_tag = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotent_requests", x => new { x.tenant_id, x.principal, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotent_requests_created_at",
                schema: "iam",
                table: "idempotent_requests",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotent_requests",
                schema: "iam");
        }
    }
}
