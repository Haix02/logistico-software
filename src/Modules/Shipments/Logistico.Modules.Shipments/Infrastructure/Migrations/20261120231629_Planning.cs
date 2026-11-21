using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Planning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "courier_run_stops",
                schema: "shipment",
                columns: table => new
                {
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    planned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_courier_run_stops", x => x.shipment_id);
                    table.ForeignKey(
                        name: "fk_courier_run_stops_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "shipment",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "planning_runs",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    hub_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    quality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    gap = table.Column<double>(type: "double precision", nullable: true),
                    seconds = table.Column<double>(type: "double precision", nullable: false),
                    solver = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    seed = table.Column<int>(type: "integer", nullable: false),
                    input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    violations = table.Column<int>(type: "integer", nullable: false),
                    cost_iqd = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: true),
                    km = table.Column<double>(type: "double precision", nullable: true),
                    parcels = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    applied_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planning_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "planning_settings",
                schema: "shipment",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_planning_settings", x => x.tenant_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_courier_run_stops_tenant_id_courier_label",
                schema: "shipment",
                table: "courier_run_stops",
                columns: new[] { "tenant_id", "courier_label" });

            migrationBuilder.CreateIndex(
                name: "ix_planning_runs_tenant_id_kind_requested_at",
                schema: "shipment",
                table: "planning_runs",
                columns: new[] { "tenant_id", "kind", "requested_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "courier_run_stops",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "planning_runs",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "planning_settings",
                schema: "shipment");
        }
    }
}
