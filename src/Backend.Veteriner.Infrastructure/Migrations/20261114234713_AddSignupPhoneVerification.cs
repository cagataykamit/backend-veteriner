using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSignupPhoneVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "varchar(12)",
                unicode: false,
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneVerifiedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SignupPhoneVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Phone = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    CodeHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RequestIpHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignupTokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    SignupTokenExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedByTenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupPhoneVerifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignupPhoneVerifications_CreatedAtUtc",
                table: "SignupPhoneVerifications",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SignupPhoneVerifications_Phone_CreatedAtUtc",
                table: "SignupPhoneVerifications",
                columns: new[] { "Phone", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SignupPhoneVerifications_RequestIpHash_CreatedAtUtc",
                table: "SignupPhoneVerifications",
                columns: new[] { "RequestIpHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_SignupPhoneVerifications_Phone_Used",
                table: "SignupPhoneVerifications",
                column: "Phone",
                unique: true,
                filter: "[UsedAtUtc] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignupPhoneVerifications");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneVerifiedAtUtc",
                table: "Users");
        }
    }
}
