using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SortPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:CollationDefinition:public.natural", "und-u-kn-true,und-u-kn-true,icu,True")
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AlterColumn<string>(
                name: "warehouse_bin",
                schema: "shipment",
                table: "shipments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                collation: "natural",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "system_bin",
                schema: "shipment",
                table: "shelf_check_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                collation: "natural",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "shelf_rules",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hub_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    to_hub_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    governorate = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    area_folded = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merchant_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    shelves = table.Column<List<string>>(type: "text[]", nullable: false),
                    capacity_per_shelf = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    specificity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shelf_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shelf_rules_tenant_id_hub_code",
                schema: "shipment",
                table: "shelf_rules",
                columns: new[] { "tenant_id", "hub_code" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shelf_rules",
                schema: "shipment");

            migrationBuilder.AlterColumn<string>(
                name: "warehouse_bin",
                schema: "shipment",
                table: "shipments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldCollation: "natural");

            migrationBuilder.AlterColumn<string>(
                name: "system_bin",
                schema: "shipment",
                table: "shelf_check_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true,
                oldCollation: "natural");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:CollationDefinition:public.natural", "und-u-kn-true,und-u-kn-true,icu,True")
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
