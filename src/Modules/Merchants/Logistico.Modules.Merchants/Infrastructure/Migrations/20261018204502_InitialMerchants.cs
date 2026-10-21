using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Merchants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMerchants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "merchant");

            migrationBuilder.CreateTable(
                name: "merchants",
                schema: "merchant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    instagram_handle = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    contact_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    governorate = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merchants", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_merchants_tenant_id_instagram_handle",
                schema: "merchant",
                table: "merchants",
                columns: new[] { "tenant_id", "instagram_handle" },
                unique: true,
                filter: "instagram_handle IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_merchants_tenant_id_name",
                schema: "merchant",
                table: "merchants",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_merchants_tenant_id_phone",
                schema: "merchant",
                table: "merchants",
                columns: new[] { "tenant_id", "phone" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchants",
                schema: "merchant");
        }
    }
}
