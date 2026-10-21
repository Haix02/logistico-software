using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rated_at",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rating",
                schema: "shipment",
                table: "shipments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rating_comment",
                schema: "shipment",
                table: "shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_rated_at",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "rated_at" },
                filter: "rated_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_rated_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "rated_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "rating",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "rating_comment",
                schema: "shipment",
                table: "shipments");
        }
    }
}
