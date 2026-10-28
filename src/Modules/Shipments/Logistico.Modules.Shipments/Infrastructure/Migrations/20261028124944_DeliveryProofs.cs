using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryProofs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_proofs",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivered = table.Column<bool>(type: "boolean", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    courier_label = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    received_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    receiver_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    signature = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    accuracy_meters = table.Column<double>(type: "double precision", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_proofs", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_proofs_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "shipment",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_proofs_shipment_id",
                schema: "shipment",
                table: "delivery_proofs",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_proofs_tenant_id_shipment_id_recorded_at",
                schema: "shipment",
                table: "delivery_proofs",
                columns: new[] { "tenant_id", "shipment_id", "recorded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_proofs",
                schema: "shipment");
        }
    }
}
