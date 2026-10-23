using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReturnFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "return_fee",
                schema: "shipment",
                table: "shipments",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "return_fee_percent",
                schema: "shipment",
                table: "rate_cards",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "return_count",
                schema: "shipment",
                table: "cod_settlements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "return_fees_amount",
                schema: "shipment",
                table: "cod_settlements",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "return_fee",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "return_fee_percent",
                schema: "shipment",
                table: "rate_cards");

            migrationBuilder.DropColumn(
                name: "return_count",
                schema: "shipment",
                table: "cod_settlements");

            migrationBuilder.DropColumn(
                name: "return_fees_amount",
                schema: "shipment",
                table: "cod_settlements");
        }
    }
}
