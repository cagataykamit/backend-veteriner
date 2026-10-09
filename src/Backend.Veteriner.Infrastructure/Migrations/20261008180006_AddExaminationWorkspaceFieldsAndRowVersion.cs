using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExaminationWorkspaceFieldsAndRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Anamnesis",
                table: "Examinations",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HeartRateBpm",
                table: "Examinations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Examinations",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RespiratoryRatePerMin",
                table: "Examinations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Examinations",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<decimal>(
                name: "TemperatureC",
                table: "Examinations",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VitalsMeasuredAtUtc",
                table: "Examinations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightKg",
                table: "Examinations",
                type: "decimal(9,3)",
                precision: 9,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Anamnesis",
                table: "Examinations");

            migrationBuilder.DropColumn(
                name: "HeartRateBpm",
                table: "Examinations");

            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Examinations");

            migrationBuilder.DropColumn(
                name: "RespiratoryRatePerMin",
                table: "Examinations");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Examinations");

            migrationBuilder.DropColumn(
                name: "TemperatureC",
                table: "Examinations");

            migrationBuilder.DropColumn(
                name: "VitalsMeasuredAtUtc",
                table: "Examinations");

            migrationBuilder.DropColumn(
                name: "WeightKg",
                table: "Examinations");
        }
    }
}
