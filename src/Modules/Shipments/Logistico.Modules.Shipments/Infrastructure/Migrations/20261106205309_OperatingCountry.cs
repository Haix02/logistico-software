using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OperatingCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operating_countries",
                schema: "shipment",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operating_countries", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "service_areas",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    name_en = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name_ar = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_areas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_open_created",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "status", "created_at" },
                filter: "status NOT IN ('Delivered', 'Returned', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_service_areas_tenant_id_code",
                schema: "shipment",
                table: "service_areas",
                columns: new[] { "tenant_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operating_countries",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "service_areas",
                schema: "shipment");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_open_created",
                schema: "shipment",
                table: "shipments");
        }
    }
}
