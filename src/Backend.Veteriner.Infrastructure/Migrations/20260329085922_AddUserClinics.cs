using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserClinics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserClinics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClinics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClinics_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserClinics_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserClinics_ClinicId",
                table: "UserClinics",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_UserClinics_UserId_ClinicId",
                table: "UserClinics",
                columns: new[] { "UserId", "ClinicId" },
                unique: true);

            // Mevcut UserTenant üyelikleri için tenant'taki aktif kliniklere idempotent atama
            migrationBuilder.Sql(
                """
                INSERT INTO UserClinics (Id, UserId, ClinicId, CreatedAtUtc)
                SELECT NEWID(), ut.UserId, c.Id, GETUTCDATE()
                FROM UserTenants ut
                INNER JOIN Clinics c ON c.TenantId = ut.TenantId AND c.IsActive = 1
                WHERE NOT EXISTS (
                    SELECT 1 FROM UserClinics uc
                    WHERE uc.UserId = ut.UserId AND uc.ClinicId = c.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserClinics");
        }
    }
}
