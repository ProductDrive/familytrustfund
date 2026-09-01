using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTrustFund.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRepaymentFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RepaymentTerm",
                table: "loans",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.CreateTable(
                name: "loan_schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loan_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loan_schedules_loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleVersion = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ActualAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Surplus = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_repayments_loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_schedule_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    DueDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PrincipalDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    InterestDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loan_schedule_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loan_schedule_items_loan_schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "loan_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_loan_schedule_items_ScheduleId",
                table: "loan_schedule_items",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_loan_schedule_items_Status",
                table: "loan_schedule_items",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_loan_schedules_LoanId",
                table: "loan_schedules",
                column: "LoanId");

            migrationBuilder.CreateIndex(
                name: "IX_loan_schedules_LoanId_Version",
                table: "loan_schedules",
                columns: new[] { "LoanId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_repayments_LoanId",
                table: "repayments",
                column: "LoanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loan_schedule_items");

            migrationBuilder.DropTable(
                name: "repayments");

            migrationBuilder.DropTable(
                name: "loan_schedules");

            migrationBuilder.DropColumn(
                name: "RepaymentTerm",
                table: "loans");
        }
    }
}
