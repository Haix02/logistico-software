using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Modules.Merchants.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FoldedSearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "search_text",
                schema: "merchant",
                table: "merchants",
                type: "text",
                nullable: true,
                computedColumnSql: "translate(lower(' ' || name || ' ' || coalesce(instagram_handle, '') || ' ' || contact_name || ' ' || phone), 'أإآٱىةؤئ٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹ـًٌٍَُِّْٰ', 'اااايهوي01234567890123456789')",
                stored: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "search_text",
                schema: "merchant",
                table: "merchants");
        }
    }
}
