using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing orders need no confirmation; existing companies confirm orders to risky customers, 12 h before an alert.
            migrationBuilder.AddColumn<string>(
                name: "confirmation",
                schema: "shipment",
                table: "shipments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<int>(
                name: "confirmation_attempts",
                schema: "shipment",
                table: "shipments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "confirmation_by",
                schema: "shipment",
                table: "shipments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmation_decided_at",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "confirmation_requested_at",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "auto_cancel_unconfirmed_hours",
                schema: "shipment",
                table: "automation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "confirmation_hours",
                schema: "shipment",
                table: "automation_settings",
                type: "integer",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<string>(
                name: "order_confirmation",
                schema: "shipment",
                table: "automation_settings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "RiskyCustomers");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_confirmation_requested_at",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "confirmation_requested_at" },
                filter: "confirmation = 'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_confirmation_requested_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "confirmation",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "confirmation_attempts",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "confirmation_by",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "confirmation_decided_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "confirmation_requested_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "auto_cancel_unconfirmed_hours",
                schema: "shipment",
                table: "automation_settings");

            migrationBuilder.DropColumn(
                name: "confirmation_hours",
                schema: "shipment",
                table: "automation_settings");

            migrationBuilder.DropColumn(
                name: "order_confirmation",
                schema: "shipment",
                table: "automation_settings");
        }
    }
}
