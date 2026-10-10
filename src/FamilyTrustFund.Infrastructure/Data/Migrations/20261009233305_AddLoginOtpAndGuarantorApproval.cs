using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyTrustFund.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLoginOtpAndGuarantorApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GuarantorApprovalStatus",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "login_otp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RequestedRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CodeSalt = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_login_otp", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "terms_acceptance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_terms_acceptance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_terms_acceptance_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_login_otp_Email",
                table: "login_otp",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_login_otp_Email_ConsumedAtUtc",
                table: "login_otp",
                columns: new[] { "Email", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_terms_acceptance_UserId_Version",
                table: "terms_acceptance",
                columns: new[] { "UserId", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "login_otp");

            migrationBuilder.DropTable(
                name: "terms_acceptance");

            migrationBuilder.DropColumn(
                name: "GuarantorApprovalStatus",
                table: "AspNetUsers");
        }
    }
}
