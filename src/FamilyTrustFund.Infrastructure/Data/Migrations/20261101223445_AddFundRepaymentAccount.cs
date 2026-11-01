using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTrustFund.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundRepaymentAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RepaymentAccountName",
                table: "funds",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepaymentAccountNumber",
                table: "funds",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepaymentBankName",
                table: "funds",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepaymentAccountName",
                table: "funds");

            migrationBuilder.DropColumn(
                name: "RepaymentAccountNumber",
                table: "funds");

            migrationBuilder.DropColumn(
                name: "RepaymentBankName",
                table: "funds");
        }
    }
}
