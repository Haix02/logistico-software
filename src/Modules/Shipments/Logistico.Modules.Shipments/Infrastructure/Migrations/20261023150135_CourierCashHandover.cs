using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CourierCashHandover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "cash_handover_seq",
                schema: "shipment");

            migrationBuilder.AddColumn<Guid>(
                name: "cash_handover_id",
                schema: "shipment",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cod_handed_over_at",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cash_handovers",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    parcel_count = table.Column<int>(type: "integer", nullable: false),
                    expected_iqd = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    received_iqd = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    difference_iqd = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_handovers", x => x.id);
                });

            // Cash was never tracked before: everything collected so far counts as handed in, so no courier suddenly owes
            // years of deliveries. New deliveries are tracked from now on.
            migrationBuilder.Sql("""
                UPDATE shipment.shipments
                SET cod_handed_over_at = coalesce(cod_settled_at, delivered_at, cod_collected_at)
                WHERE cod_collected_at IS NOT NULL AND cod_handed_over_at IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_cash_with_couriers",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "courier_name" },
                filter: "cod_collected_at IS NOT NULL AND cod_handed_over_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cash_handovers_tenant_id_courier_id_received_at",
                schema: "shipment",
                table: "cash_handovers",
                columns: new[] { "tenant_id", "courier_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cash_handovers_tenant_id_number",
                schema: "shipment",
                table: "cash_handovers",
                columns: new[] { "tenant_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_handovers",
                schema: "shipment");

            migrationBuilder.DropIndex(
                name: "ix_shipments_cash_with_couriers",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "cash_handover_id",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "cod_handed_over_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropSequence(
                name: "cash_handover_seq",
                schema: "shipment");
        }
    }
}
