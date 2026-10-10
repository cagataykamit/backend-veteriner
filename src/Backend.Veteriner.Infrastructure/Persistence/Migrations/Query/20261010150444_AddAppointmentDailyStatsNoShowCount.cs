using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Persistence.Migrations.Query
{
    /// <inheritdoc />
    public partial class AddAppointmentDailyStatsNoShowCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NoShowCount",
                table: "ClinicDailyAppointmentStatsReadModels",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NoShowCount",
                table: "ClinicDailyAppointmentStatsReadModels");
        }
    }
}
