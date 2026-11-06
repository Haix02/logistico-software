using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistico.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompanyCountry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "country",
                schema: "iam",
                table: "company_profiles",
                type: "character(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: false,
                defaultValue: "IQ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "country",
                schema: "iam",
                table: "company_profiles");
        }
    }
}
