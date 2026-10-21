using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_cod_collected_at",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "cod_collected_at" },
                filter: "cod_collected_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_delivered_at",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "delivered_at" },
                filter: "delivered_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_return_requested_at",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "return_requested_at" },
                filter: "return_requested_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_events_tenant_status_occurred",
                schema: "shipment",
                table: "shipment_events",
                columns: new[] { "tenant_id", "status", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_cod_collected_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_delivered_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_return_requested_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipment_events_tenant_status_occurred",
                schema: "shipment",
                table: "shipment_events");
        }
    }
}
