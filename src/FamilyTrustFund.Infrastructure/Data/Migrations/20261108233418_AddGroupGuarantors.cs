using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTrustFund.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupGuarantors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_funds_GuarantorId_JoinCode",
                table: "funds");

            migrationBuilder.AlterColumn<string>(
                name: "ConfirmationNote",
                table: "pending_repayment",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                table: "pending_repayment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByUserId",
                table: "pending_repayment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "pending_repayment",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsFrozenAtUtc",
                table: "loans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "loans",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "GuarantorMode",
                table: "funds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "ConfirmationNote",
                table: "fund_contributions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmedByUserId",
                table: "fund_contributions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByUserId",
                table: "fund_contributions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "fund_contributions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "fund_guarantors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FundId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuarantorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CommittedCapital = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    InvitedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvitedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RespondedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fund_guarantors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fund_guarantors_AspNetUsers_GuarantorId",
                        column: x => x.GuarantorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fund_guarantors_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fund_guarantors_funds_FundId",
                        column: x => x.FundId,
                        principalTable: "funds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "loan_approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuarantorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loan_approvals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_loan_approvals_AspNetUsers_GuarantorId",
                        column: x => x.GuarantorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_loan_approvals_loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_funds_JoinCode",
                table: "funds",
                column: "JoinCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fund_guarantors_FundId_GuarantorId",
                table: "fund_guarantors",
                columns: new[] { "FundId", "GuarantorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fund_guarantors_FundId_Status",
                table: "fund_guarantors",
                columns: new[] { "FundId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_fund_guarantors_GuarantorId_Status",
                table: "fund_guarantors",
                columns: new[] { "GuarantorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_fund_guarantors_InvitedByUserId",
                table: "fund_guarantors",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_loan_approvals_GuarantorId_Status",
                table: "loan_approvals",
                columns: new[] { "GuarantorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_loan_approvals_LoanId_GuarantorId",
                table: "loan_approvals",
                columns: new[] { "LoanId", "GuarantorId" },
                unique: true);

            // Backfill: existing pending loans on single-Guarantor funds need one
            // approval requirement for their owner so the unanimous-approval flow
            // has a row to decide on. Existing funds stay single-Guarantor.
            migrationBuilder.Sql(
                """
                INSERT INTO loan_approvals ("Id", "LoanId", "GuarantorId", "Status", "DecidedAtUtc", "Comment", "CreatedAtUtc")
                SELECT gen_random_uuid(), l."Id", f."GuarantorId", 1, NULL, NULL, NOW()
                FROM loans l
                INNER JOIN funds f ON f."Id" = l."FundId"
                WHERE l."Status" = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fund_guarantors");

            migrationBuilder.DropTable(
                name: "loan_approvals");

            migrationBuilder.DropIndex(
                name: "IX_funds_JoinCode",
                table: "funds");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "pending_repayment");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "pending_repayment");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "pending_repayment");

            migrationBuilder.DropColumn(
                name: "TermsFrozenAtUtc",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "loans");

            migrationBuilder.DropColumn(
                name: "GuarantorMode",
                table: "funds");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "fund_contributions");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "fund_contributions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "fund_contributions");

            migrationBuilder.AlterColumn<string>(
                name: "ConfirmationNote",
                table: "pending_repayment",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ConfirmationNote",
                table: "fund_contributions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_funds_GuarantorId_JoinCode",
                table: "funds",
                columns: new[] { "GuarantorId", "JoinCode" },
                unique: true);
        }
    }
}
