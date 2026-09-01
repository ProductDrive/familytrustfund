using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTrustFund.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRecipientAndDisbursement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_recipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BankCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BankName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderRecipientCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_recipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payment_recipients_AspNetUsers_MemberId",
                        column: x => x.MemberId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "disbursement_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LoanId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastProcessedEventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InitiatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_disbursement_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_disbursement_transactions_loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_disbursement_transactions_payment_recipients_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "payment_recipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_disbursement_transactions_IdempotencyKey",
                table: "disbursement_transactions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_disbursement_transactions_LastProcessedEventId",
                table: "disbursement_transactions",
                column: "LastProcessedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_disbursement_transactions_LoanId",
                table: "disbursement_transactions",
                column: "LoanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_disbursement_transactions_ProviderReference",
                table: "disbursement_transactions",
                column: "ProviderReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_disbursement_transactions_RecipientId",
                table: "disbursement_transactions",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_disbursement_transactions_Status",
                table: "disbursement_transactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_payment_recipients_MemberId",
                table: "payment_recipients",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_payment_recipients_MemberId_IsActive",
                table: "payment_recipients",
                columns: new[] { "MemberId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disbursement_transactions");

            migrationBuilder.DropTable(
                name: "payment_recipients");
        }
    }
}
