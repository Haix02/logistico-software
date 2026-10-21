using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OperationsModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cod_settled_at",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cod_settlement_id",
                schema: "shipment",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cod_settlements",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    shipment_count = table.Column<int>(type: "integer", nullable: false),
                    gross_cod_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    delivery_fees_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    net_payout_amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    payout_method = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payout_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    settled_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cod_settlements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "couriers",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    governorate = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    vehicle_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    vehicle_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_couriers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customs_declarations",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customs_office_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    regime = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    declaration_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    assessment_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    receipt_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    release_order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    selectivity_lane = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    hs_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    goods_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    country_of_origin = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    coo_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    coo_e_verified = table.Column<bool>(type: "boolean", nullable: false),
                    commercial_invoice_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    invoice_e_verified = table.Column<bool>(type: "boolean", nullable: false),
                    coc_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    customs_value_usd = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    duty_rate_percent = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: false),
                    assessed_duty_iqd = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    tax_and_fees_iqd = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    hold_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    broker_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customs_declarations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_cod_settlement_id",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "cod_settlement_id" },
                filter: "cod_settlement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_cod_settlements_tenant_id_merchant_id_settled_at",
                schema: "shipment",
                table: "cod_settlements",
                columns: new[] { "tenant_id", "merchant_id", "settled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_cod_settlements_tenant_id_settlement_number",
                schema: "shipment",
                table: "cod_settlements",
                columns: new[] { "tenant_id", "settlement_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_couriers_tenant_id_code",
                schema: "shipment",
                table: "couriers",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_couriers_tenant_id_is_active_governorate",
                schema: "shipment",
                table: "couriers",
                columns: new[] { "tenant_id", "is_active", "governorate" });

            migrationBuilder.CreateIndex(
                name: "ix_customs_declarations_tenant_id_declaration_number",
                schema: "shipment",
                table: "customs_declarations",
                columns: new[] { "tenant_id", "declaration_number" },
                filter: "declaration_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customs_declarations_tenant_id_shipment_id",
                schema: "shipment",
                table: "customs_declarations",
                columns: new[] { "tenant_id", "shipment_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cod_settlements",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "couriers",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "customs_declarations",
                schema: "shipment");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_cod_settlement_id",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "cod_settled_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "cod_settlement_id",
                schema: "shipment",
                table: "shipments");
        }
    }
}
