using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataMigrationSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceProfile",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MigrationDays",
                table: "CustomDevelopmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MigrationFee",
                table: "CustomDevelopmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MigrationSourceProgram",
                table: "CustomDevelopmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MigrationTimes",
                table: "CustomDevelopmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MigrationVolume",
                table: "CustomDevelopmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CustomDevelopmentRequests_Migration_Details",
                table: "CustomDevelopmentRequests",
                sql: "([TargetKind] = 3 AND [MigrationSourceProgram] IS NOT NULL AND [MigrationVolume] IS NOT NULL AND [MigrationDays] IS NOT NULL AND [MigrationTimes] IS NOT NULL AND [MigrationFee] IS NOT NULL) OR ([TargetKind] <> 3 AND [MigrationSourceProgram] IS NULL AND [MigrationVolume] IS NULL AND [MigrationDays] IS NULL AND [MigrationTimes] IS NULL AND [MigrationFee] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CustomDevelopmentRequests_Migration_Details",
                table: "CustomDevelopmentRequests");

            migrationBuilder.DropColumn(
                name: "SourceProfile",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "MigrationDays",
                table: "CustomDevelopmentRequests");

            migrationBuilder.DropColumn(
                name: "MigrationFee",
                table: "CustomDevelopmentRequests");

            migrationBuilder.DropColumn(
                name: "MigrationSourceProgram",
                table: "CustomDevelopmentRequests");

            migrationBuilder.DropColumn(
                name: "MigrationTimes",
                table: "CustomDevelopmentRequests");

            migrationBuilder.DropColumn(
                name: "MigrationVolume",
                table: "CustomDevelopmentRequests");
        }
    }
}
