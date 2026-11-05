using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTrustFund.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLoanTermsAndFundTermRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovedTermMonths",
                table: "loans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedInstalments",
                table: "loans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestedTermMonths",
                table: "loans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxLoanTermMonths",
                table: "funds",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "MinLoanTermMonths",
                table: "funds",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedTermMonths",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "RequestedInstalments",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "RequestedTermMonths",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "MaxLoanTermMonths",
                table: "funds");

            migrationBuilder.DropColumn(
                name: "MinLoanTermMonths",
                table: "funds");
        }
    }
}
