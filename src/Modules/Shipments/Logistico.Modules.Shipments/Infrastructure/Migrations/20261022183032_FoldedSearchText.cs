using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Shipments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FoldedSearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shipments_consignee_name",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropIndex(
                name: "ix_shipments_merchant_name",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                schema: "shipment",
                table: "shipments",
                type: "text",
                nullable: true,
                computedColumnSql: "translate(lower(' ' || tracking_number || ' ' || consignee_name || ' ' || coalesce(consignee_phone, '') || ' ' || coalesce(merchant_name, '') || ' ' || shipper_name || ' ' || coalesce(external_reference, '') || ' ' || coalesce(destination_city, '') || ' ' || CASE destination_code WHEN 'IQ-BG' THEN 'Baghdad بغداد' WHEN 'IQ-BA' THEN 'Basra البصرة' WHEN 'IQ-NI' THEN 'Nineveh نينوى' WHEN 'IQ-AR' THEN 'Erbil أربيل' WHEN 'IQ-SU' THEN 'Sulaymaniyah السليمانية' WHEN 'IQ-DA' THEN 'Duhok دهوك' WHEN 'IQ-KI' THEN 'Kirkuk كركوك' WHEN 'IQ-NA' THEN 'Najaf النجف' WHEN 'IQ-KA' THEN 'Karbala كربلاء' WHEN 'IQ-BB' THEN 'Babil بابل' WHEN 'IQ-DI' THEN 'Diyala ديالى' WHEN 'IQ-AN' THEN 'Anbar الأنبار' WHEN 'IQ-SD' THEN 'Saladin صلاح الدين' WHEN 'IQ-WA' THEN 'Wasit واسط' WHEN 'IQ-QA' THEN 'Al-Qadisiyah القادسية' WHEN 'IQ-MU' THEN 'Muthanna المثنى' WHEN 'IQ-DQ' THEN 'Dhi Qar ذي قار' WHEN 'IQ-MA' THEN 'Maysan ميسان' WHEN 'IQ-HA' THEN 'Halabja حلبجة' ELSE '' END), 'أإآٱىةؤئ٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹ـًٌٍَُِّْٰ', 'اااايهوي01234567890123456789')",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipments_search_text",
                schema: "shipment",
                table: "shipments",
                column: "search_text")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shipments_search_text",
                schema: "shipment",
                table: "shipments");

            migrationBuilder.DropColumn(
                name: "search_text",
                schema: "shipment",
                table: "shipments");

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
        }
    }
}
