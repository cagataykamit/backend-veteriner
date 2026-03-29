using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleUserTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kiracı başına tek UserTenant satırı (deterministik: CreatedAtUtc, TenantId)
            migrationBuilder.Sql(
                """
                ;WITH d AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (PARTITION BY UserId ORDER BY CreatedAtUtc, TenantId) AS rn
                    FROM UserTenants
                )
                DELETE FROM UserTenants WHERE Id IN (SELECT Id FROM d WHERE rn > 1);
                """);

            // Kiracı uyumsuz UserClinic atamaları (veri bütünlüğü)
            migrationBuilder.Sql(
                """
                DELETE uc
                FROM UserClinics uc
                INNER JOIN Clinics c ON c.Id = uc.ClinicId
                INNER JOIN UserTenants ut ON ut.UserId = uc.UserId
                WHERE ut.TenantId <> c.TenantId;
                """);

            migrationBuilder.DropIndex(
                name: "IX_UserTenants_UserId_TenantId",
                table: "UserTenants");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenants_UserId",
                table: "UserTenants",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTenants_UserId_TenantId",
                table: "UserTenants",
                columns: new[] { "UserId", "TenantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserTenants_UserId",
                table: "UserTenants");

            migrationBuilder.DropIndex(
                name: "IX_UserTenants_UserId_TenantId",
                table: "UserTenants");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenants_UserId_TenantId",
                table: "UserTenants",
                columns: new[] { "UserId", "TenantId" },
                unique: true);
        }
    }
}
