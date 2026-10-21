using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HubNetworkAndAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "request_number_seq",
                schema: "shipment");

            migrationBuilder.CreateSequence(
                name: "return_manifest_seq",
                schema: "shipment");

            migrationBuilder.CreateSequence(
                name: "settlement_number_seq",
                schema: "shipment");

            migrationBuilder.CreateSequence(
                name: "transfer_number_seq",
                schema: "shipment");

            migrationBuilder.AddColumn<string>(
                name: "current_hub_code",
                schema: "shipment",
                table: "shipments",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "destination_hub_code",
                schema: "shipment",
                table: "shipments",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "HUB-BGW");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_status_at",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "origin_hub_code",
                schema: "shipment",
                table: "shipments",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rescheduled_to",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_manifest_number",
                schema: "shipment",
                table: "shipments",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_reason",
                schema: "shipment",
                table: "shipments",
                type: "character varying(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "return_requested_at",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "returned_at",
                schema: "shipment",
                table: "shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_id",
                schema: "shipment",
                table: "shipments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alerts",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    subject_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transfer_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    hub_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    governorate = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    hours = table.Column<int>(type: "integer", nullable: true),
                    detail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    auto_resolved = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "automation_log",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rule = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    detail = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "automation_settings",
                schema: "shipment",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_delivery_attempts = table.Column<int>(type: "integer", nullable: false),
                    return_on_refusal = table.Column<bool>(type: "boolean", nullable: false),
                    auto_reschedule = table.Column<bool>(type: "boolean", nullable: false),
                    auto_plan_transfers = table.Column<bool>(type: "boolean", nullable: false),
                    auto_apply_customer_requests = table.Column<bool>(type: "boolean", nullable: false),
                    notify_customers = table.Column<bool>(type: "boolean", nullable: false),
                    pickup_sla_hours = table.Column<int>(type: "integer", nullable: false),
                    warehouse_dwell_hours = table.Column<int>(type: "integer", nullable: false),
                    out_for_delivery_hours = table.Column<int>(type: "integer", nullable: false),
                    line_haul_grace_hours = table.Column<int>(type: "integer", nullable: false),
                    return_collection_days = table.Column<int>(type: "integer", nullable: false),
                    request_sla_hours = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_sweep_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_settings", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "customer_notifications",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    template = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    phone = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hub_scans",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scanned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    hub_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ok = table.Column<bool>(type: "boolean", nullable: false),
                    is_misrouted = table.Column<bool>(type: "boolean", nullable: false),
                    expected_hub_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    warehouse_bin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    transfer_number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    courier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    message = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    problem_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    scanned_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hub_scans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_requests",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    preferred_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    new_governorate = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    new_city = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    new_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    new_landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    new_phone = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    resolution = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    from_hub_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    to_hub_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    vehicle_plate = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    driver_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    seal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    departed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    arrived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    departed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    received_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfer_items",
                schema: "shipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tracking_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_return = table.Column<bool>(type: "boolean", nullable: false),
                    planned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    loaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfer_items_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalSchema: "shipment",
                        principalTable: "transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_current_hub_code_status",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "current_hub_code", "status" },
                filter: "current_hub_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_destination_code_status",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "destination_code", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_return_manifest_number",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "return_manifest_number" },
                filter: "return_manifest_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_tenant_id_status_last_status_at",
                schema: "shipment",
                table: "shipments",
                columns: new[] { "tenant_id", "status", "last_status_at" });

            migrationBuilder.CreateIndex(
                name: "ix_alerts_tenant_id_resolved_at_raised_at",
                schema: "shipment",
                table: "alerts",
                columns: new[] { "tenant_id", "resolved_at", "raised_at" });

            migrationBuilder.CreateIndex(
                name: "ix_alerts_tenant_id_subject_key",
                schema: "shipment",
                table: "alerts",
                columns: new[] { "tenant_id", "subject_key" },
                unique: true,
                filter: "resolved_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_automation_log_tenant_id_at",
                schema: "shipment",
                table: "automation_log",
                columns: new[] { "tenant_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_notifications_tenant_id_shipment_id",
                schema: "shipment",
                table: "customer_notifications",
                columns: new[] { "tenant_id", "shipment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customer_notifications_tenant_id_status_created_at",
                schema: "shipment",
                table: "customer_notifications",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_hub_scans_tenant_id_hub_code_scanned_at",
                schema: "shipment",
                table: "hub_scans",
                columns: new[] { "tenant_id", "hub_code", "scanned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_hub_scans_tenant_id_id",
                schema: "shipment",
                table: "hub_scans",
                columns: new[] { "tenant_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_tenant_id_number",
                schema: "shipment",
                table: "service_requests",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_tenant_id_shipment_id_created_at",
                schema: "shipment",
                table: "service_requests",
                columns: new[] { "tenant_id", "shipment_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_tenant_id_status_due_at",
                schema: "shipment",
                table: "service_requests",
                columns: new[] { "tenant_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_items_tenant_id_shipment_id",
                schema: "shipment",
                table: "transfer_items",
                columns: new[] { "tenant_id", "shipment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transfer_items_transfer_id_shipment_id",
                schema: "shipment",
                table: "transfer_items",
                columns: new[] { "transfer_id", "shipment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfers_tenant_id_number",
                schema: "shipment",
                table: "transfers",
                columns: new[] { "tenant_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfers_tenant_id_status_from_hub_code_to_hub_code",
                schema: "shipment",
                table: "transfers",
                columns: new[] { "tenant_id", "status", "from_hub_code", "to_hub_code" });

            // Existing parcels: hubs from their governorates (same mapping as IraqHubs.ResolveDestinationHub),
            // parcels already on a shelf are at their destination hub, and the status clock starts at the last update.
            const string hubOf = """
                CASE {0}
                    WHEN 'IQ-BA' THEN 'HUB-BSR' WHEN 'IQ-DQ' THEN 'HUB-BSR' WHEN 'IQ-MA' THEN 'HUB-BSR' WHEN 'IQ-MU' THEN 'HUB-BSR' WHEN 'IQUQR' THEN 'HUB-BSR' WHEN 'IQBSR' THEN 'HUB-BSR'
                    WHEN 'IQ-AR' THEN 'HUB-EBL' WHEN 'IQ-SU' THEN 'HUB-EBL' WHEN 'IQ-DA' THEN 'HUB-EBL' WHEN 'IQ-KI' THEN 'HUB-EBL' WHEN 'IQEBL' THEN 'HUB-EBL' WHEN 'IQISU' THEN 'HUB-EBL' WHEN 'IQ-HA' THEN 'HUB-EBL'
                    WHEN 'IQ-NA' THEN 'HUB-NJF' WHEN 'IQ-KA' THEN 'HUB-NJF' WHEN 'IQ-BB' THEN 'HUB-NJF' WHEN 'IQ-QA' THEN 'HUB-NJF' WHEN 'IQNJF' THEN 'HUB-NJF'
                    WHEN 'IQ-NI' THEN 'HUB-MOS'
                    ELSE 'HUB-BGW'
                END
                """;
            migrationBuilder.Sql($"UPDATE shipment.shipments SET destination_hub_code = {string.Format(hubOf, "destination_code")};");
            migrationBuilder.Sql($"UPDATE shipment.shipments SET origin_hub_code = {string.Format(hubOf, "origin_code")} WHERE origin_code LIKE 'IQ%';");
            migrationBuilder.Sql("UPDATE shipment.shipments SET current_hub_code = destination_hub_code WHERE status IN ('AtWarehouse', 'ReadyForDispatch', 'AtDestinationWarehouse');");
            migrationBuilder.Sql("UPDATE shipment.shipments SET last_status_at = updated_at;");

            // Settlement numbers now come from a sequence; continue after the batches that already exist.
            migrationBuilder.Sql("SELECT setval('shipment.settlement_number_seq', GREATEST((SELECT COUNT(*) FROM shipment.cod_settlements), 1), (SELECT COUNT(*) FROM shipment.cod_settlements) > 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "automation_log",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "automation_settings",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "customer_notifications",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "hub_scans",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "service_requests",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "transfer_items",
                schema: "shipment");

            migrationBuilder.DropTable(
                name: "transfers",
                schema: "shipment");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_current_hub_code_status",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_destination_code_status",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_return_manifest_number",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_tenant_id_status_last_status_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "current_hub_code",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "destination_hub_code",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "last_status_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "origin_hub_code",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "rescheduled_to",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "return_manifest_number",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "return_reason",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "return_requested_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "returned_at",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "transfer_id",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropSequence(
                name: "request_number_seq",
                schema: "shipment");

            migrationBuilder.DropSequence(
                name: "return_manifest_seq",
                schema: "shipment");

            migrationBuilder.DropSequence(
                name: "settlement_number_seq",
                schema: "shipment");

            migrationBuilder.DropSequence(
                name: "transfer_number_seq",
                schema: "shipment");
        }
    }
}
