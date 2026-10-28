using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shipment_comments",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    author_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipment_comments_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "shipment",
                        principalTable: "shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipment_comments_shipment_id",
                schema: "shipment",
                table: "shipment_comments",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_comments_tenant_id_shipment_id_created_at",
                schema: "shipment",
                table: "shipment_comments",
                columns: new[] { "tenant_id", "shipment_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipment_comments",
                schema: "shipment");
        }
    }
}
