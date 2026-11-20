using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVitalMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VitalMeasurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HospitalizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExaminationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MeasuredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TemperatureC = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: true),
                    HeartRateBpm = table.Column<int>(type: "int", nullable: true),
                    RespiratoryRatePerMin = table.Column<int>(type: "int", nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AddedAfterFinalization = table.Column<bool>(type: "bit", nullable: false),
                    LateEntryReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VitalMeasurements", x => x.Id);
                    table.CheckConstraint("CK_VitalMeasurements_Cancel_Consistent", "([CancelledAtUtc] IS NULL AND [CancelReason] IS NULL AND [CancelledByUserId] IS NULL) OR ([CancelledAtUtc] IS NOT NULL AND [CancelReason] IS NOT NULL AND [CancelledByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_VitalMeasurements_HasValue", "[TemperatureC] IS NOT NULL OR [HeartRateBpm] IS NOT NULL OR [RespiratoryRatePerMin] IS NOT NULL OR [WeightKg] IS NOT NULL");
                    table.CheckConstraint("CK_VitalMeasurements_LateEntry_Consistent", "([AddedAfterFinalization] = 0 AND [LateEntryReason] IS NULL) OR ([AddedAfterFinalization] = 1 AND [LateEntryReason] IS NOT NULL)");
                    table.CheckConstraint("CK_VitalMeasurements_SingleLink", "[HospitalizationId] IS NULL OR [ExaminationId] IS NULL");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VitalMeasurements_TenantId_ClinicId",
                table: "VitalMeasurements",
                columns: new[] { "TenantId", "ClinicId" });

            migrationBuilder.CreateIndex(
                name: "IX_VitalMeasurements_TenantId_ExaminationId",
                table: "VitalMeasurements",
                columns: new[] { "TenantId", "ExaminationId" });

            migrationBuilder.CreateIndex(
                name: "IX_VitalMeasurements_TenantId_HospitalizationId",
                table: "VitalMeasurements",
                columns: new[] { "TenantId", "HospitalizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_VitalMeasurements_TenantId_PetId_MeasuredAtUtc",
                table: "VitalMeasurements",
                columns: new[] { "TenantId", "PetId", "MeasuredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VitalMeasurements");
        }
    }
}
