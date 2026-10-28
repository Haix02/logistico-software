using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShelfChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "shelf_check_seq",
                schema: "shipment");

            migrationBuilder.CreateTable(
                name: "shelf_checks",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hub_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    bin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    finished = table.Column<bool>(type: "boolean", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shelf_checks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shelf_check_lines",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shelf_check_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    system_hub_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    system_bin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    system_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shelf_check_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_shelf_check_lines_shelf_checks_shelf_check_id",
                        column: x => x.shelf_check_id,
                        principalSchema: "shipment",
                        principalTable: "shelf_checks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shelf_check_lines_shelf_check_id_state",
                schema: "shipment",
                table: "shelf_check_lines",
                columns: new[] { "shelf_check_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_shelf_check_lines_shelf_check_id_tracking_number",
                schema: "shipment",
                table: "shelf_check_lines",
                columns: new[] { "shelf_check_id", "tracking_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shelf_checks_tenant_id_hub_code_started_at",
                schema: "shipment",
                table: "shelf_checks",
                columns: new[] { "tenant_id", "hub_code", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shelf_checks_tenant_id_number",
                schema: "shipment",
                table: "shelf_checks",
                columns: new[] { "tenant_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shelf_check_lines",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "shelf_checks",
                schema: "shipment");

            migrationBuilder.DropSequence(
                name: "shelf_check_seq",
                schema: "shipment");
        }
    }
}
