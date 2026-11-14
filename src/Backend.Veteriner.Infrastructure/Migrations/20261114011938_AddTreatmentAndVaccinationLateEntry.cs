using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatmentAndVaccinationLateEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AddedAfterFinalization",
                table: "Vaccinations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LateEntryReason",
                table: "Vaccinations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AddedAfterFinalization",
                table: "Treatments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LateEntryReason",
                table: "Treatments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Vaccinations_LateEntry_Consistent",
                table: "Vaccinations",
                sql: "([AddedAfterFinalization] = 0 AND [LateEntryReason] IS NULL) OR ([AddedAfterFinalization] = 1 AND [LateEntryReason] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Treatments_LateEntry_Consistent",
                table: "Treatments",
                sql: "([AddedAfterFinalization] = 0 AND [LateEntryReason] IS NULL) OR ([AddedAfterFinalization] = 1 AND [LateEntryReason] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Vaccinations_LateEntry_Consistent",
                table: "Vaccinations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Treatments_LateEntry_Consistent",
                table: "Treatments");

            migrationBuilder.DropColumn(
                name: "AddedAfterFinalization",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "LateEntryReason",
                table: "Vaccinations");

            migrationBuilder.DropColumn(
                name: "AddedAfterFinalization",
                table: "Treatments");

            migrationBuilder.DropColumn(
                name: "LateEntryReason",
                table: "Treatments");
        }
    }
}
