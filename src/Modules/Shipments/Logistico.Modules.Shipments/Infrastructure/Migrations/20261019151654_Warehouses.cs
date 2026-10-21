using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Warehouses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "warehouses",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    routing_tag = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    name_en = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    governorate = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    manager_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    opening_hours = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    shelf_capacity = table.Column<int>(type: "integer", nullable: true),
                    covers = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_warehouses", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_code",
                schema: "shipment",
                table: "warehouses",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_warehouses_tenant_id_is_primary",
                schema: "shipment",
                table: "warehouses",
                columns: new[] { "tenant_id", "is_primary" },
                unique: true,
                filter: "is_primary");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "warehouses",
                schema: "shipment");
        }
    }
}
