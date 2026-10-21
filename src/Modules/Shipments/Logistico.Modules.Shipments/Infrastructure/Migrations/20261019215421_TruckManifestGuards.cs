using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TruckManifestGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "manifest_revision",
                schema: "shipment",
                table: "transfers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ux_transfers_one_loading_per_route",
                schema: "shipment",
                table: "transfers",
                columns: new[] { "tenant_id", "from_hub_code", "to_hub_code" },
                unique: true,
                filter: "status = 'Loading'");

            migrationBuilder.CreateIndex(
                name: "ix_hub_scans_tenant_id_scanned_at",
                schema: "shipment",
                table: "hub_scans",
                columns: new[] { "tenant_id", "scanned_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_transfers_one_loading_per_route",
                schema: "shipment",
                table: "transfers");

            migrationBuilder.DropIndex(
                name: "ix_hub_scans_tenant_id_scanned_at",
                schema: "shipment",
                table: "hub_scans");

            migrationBuilder.DropColumn(
                name: "manifest_revision",
                schema: "shipment",
                table: "transfers");
        }
    }
}
