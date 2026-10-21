using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DomesticDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AlterColumn<string>(
                name: "consignee_phone",
                schema: "shipment",
                table: "shipments",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cod_collected_at",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "courier_name",
                schema: "shipment",
                table: "shipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_address",
                schema: "shipment",
                table: "shipments",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delivery_attempts",
                schema: "shipment",
                table: "shipments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "delivery_fee",
                schema: "shipment",
                table: "shipments",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_city",
                schema: "shipment",
                table: "shipments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_reference",
                schema: "shipment",
                table: "shipments",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "items_description",
                schema: "shipment",
                table: "shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "landmark",
                schema: "shipment",
                table: "shipments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_failure_reason",
                schema: "shipment",
                table: "shipments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merchant_id",
                schema: "shipment",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "merchant_name",
                schema: "shipment",
                table: "shipments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pickup_required",
                schema: "shipment",
                table: "shipments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "service_type",
                schema: "shipment",
                table: "shipments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "International");

            migrationBuilder.AddColumn<string>(
                name: "warehouse_bin",
                schema: "shipment",
                table: "shipments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "courier_name",
                schema: "shipment",
                table: "shipment_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_reason",
                schema: "shipment",
                table: "shipment_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "warehouse_bin",
                schema: "shipment",
                table: "shipment_events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_consignee_name",
                schema: "shipment",
                table: "shipments",
                column: "consignee_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_merchant_name",
                schema: "shipment",
                table: "shipments",
                column: "merchant_name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_consignee_phone",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "consignee_phone" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_merchant_id_created_at",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "merchant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_warehouse_bin",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "warehouse_bin" },
                filter: "warehouse_bin IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shipments_consignee_name",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_merchant_name",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_consignee_phone",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_merchant_id_created_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_warehouse_bin",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "cod_collected_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "courier_name",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "delivery_address",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "delivery_attempts",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "delivery_fee",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "destination_city",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "external_reference",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "items_description",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "landmark",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "last_failure_reason",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "merchant_id",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "merchant_name",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "pickup_required",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "service_type",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "warehouse_bin",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "courier_name",
                schema: "shipment",
                table: "shipment_events");

            migrationBuilder.DropColumn(
                name: "failure_reason",
                schema: "shipment",
                table: "shipment_events");

            migrationBuilder.DropColumn(
                name: "warehouse_bin",
                schema: "shipment",
                table: "shipment_events");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AlterColumn<string>(
                name: "consignee_phone",
                schema: "shipment",
                table: "shipments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(24)",
                oldMaxLength: 24,
                oldNullable: true);
        }
    }
}
