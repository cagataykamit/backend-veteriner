using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Examinations_TenantId_ExaminedAtUtc",
                table: "Examinations");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_PetId_ArrivedAtUtc",
                table: "Visits",
                columns: new[] { "TenantId", "PetId", "ArrivedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "VoidedAtUtc", "CareStatus", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_TenantId_MovementType_OccurredAtUtc",
                table: "StockMovements",
                columns: new[] { "TenantId", "MovementType", "OccurredAtUtc" })
                .Annotation("SqlServer:Include", new[] { "ClinicId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId_VoidedAtUtc_Covering",
                table: "Payments",
                columns: new[] { "TenantId", "VoidedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "AppointmentId", "ExaminationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Examinations_TenantId_ExaminedAtUtc",
                table: "Examinations",
                columns: new[] { "TenantId", "ExaminedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "ClinicId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_TenantId_VoidedAtUtc_Covering",
                table: "Charges",
                columns: new[] { "TenantId", "VoidedAtUtc" })
                .Annotation("SqlServer:Include", new[] { "ClinicId", "ClientId", "Amount", "AllocatedAmount", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId_PetId_ArrivedAtUtc",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_TenantId_MovementType_OccurredAtUtc",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId_VoidedAtUtc_Covering",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Examinations_TenantId_ExaminedAtUtc",
                table: "Examinations");

            migrationBuilder.DropIndex(
                name: "IX_Charges_TenantId_VoidedAtUtc_Covering",
                table: "Charges");

            migrationBuilder.CreateIndex(
                name: "IX_Examinations_TenantId_ExaminedAtUtc",
                table: "Examinations",
                columns: new[] { "TenantId", "ExaminedAtUtc" });
        }
    }
}
