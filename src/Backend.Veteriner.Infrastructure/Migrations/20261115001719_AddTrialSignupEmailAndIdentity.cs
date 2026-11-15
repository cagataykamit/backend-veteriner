using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialSignupEmailAndIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignupEmailVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestIpHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignupTokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    SignupTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupEmailVerifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrialSignupIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxIdentityNumber = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialSignupIdentities", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignupEmailVerifications_CreatedAtUtc",
                table: "SignupEmailVerifications",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SignupEmailVerifications_Email_CreatedAtUtc",
                table: "SignupEmailVerifications",
                columns: new[] { "Email", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SignupEmailVerifications_RequestIpHash_CreatedAtUtc",
                table: "SignupEmailVerifications",
                columns: new[] { "RequestIpHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrialSignupIdentities_EmailKey",
                table: "TrialSignupIdentities",
                column: "EmailKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrialSignupIdentities_TaxIdentityNumber",
                table: "TrialSignupIdentities",
                column: "TaxIdentityNumber",
                unique: true,
                filter: "[TaxIdentityNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TrialSignupIdentities_TenantId",
                table: "TrialSignupIdentities",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignupEmailVerifications");

            migrationBuilder.DropTable(
                name: "TrialSignupIdentities");
        }
    }
}
