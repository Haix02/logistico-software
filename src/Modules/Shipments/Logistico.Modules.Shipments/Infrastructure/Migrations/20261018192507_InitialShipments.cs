using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shipment");

            migrationBuilder.CreateSequence(
                name: "tracking_number_seq",
                schema: "shipment");

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    house_bill_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    incoterm = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    shipper_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    consignee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    consignee_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    origin_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    destination_code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    pieces = table.Column<int>(type: "integer", nullable: false),
                    gross_weight_kg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    volume_cbm = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    chargeable_weight_kg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    declared_value = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    declared_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    cod_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    cod_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    promised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    eta_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shipment_events",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    recorded_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_events_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "shipment",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_events_shipment_id_occurred_at",
                schema: "shipment",
                table: "shipment_events",
                columns: new[] { "shipment_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_created_at",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_status_eta_at",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "status", "eta_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_tracking_number",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "tracking_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipment_events",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "shipment");

            migrationBuilder.DropSequence(
                name: "tracking_number_seq",
                schema: "shipment");
        }
    }
}
