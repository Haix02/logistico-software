using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RateCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rate_cards",
                schema: "shipment",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    same_governorate_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    default_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    included_weight_kg = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    extra_kg_fee = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    governorate_fees = table.Column<string>(type: "jsonb", nullable: false),
                    seller_discounts = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rate_cards", x => x.tenant_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rate_cards",
                schema: "shipment");
        }
    }
}
