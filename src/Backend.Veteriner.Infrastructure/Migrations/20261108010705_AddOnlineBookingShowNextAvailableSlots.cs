using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineBookingShowNextAvailableSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TIER-017: varsayılan açık; online randevusu açık mevcut klinikler de en yakın saati gösterir (kapatabilir).
            migrationBuilder.AddColumn<bool>(
                name: "ShowNextAvailableSlots",
                table: "ClinicOnlineBookingSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowNextAvailableSlots",
                table: "ClinicOnlineBookingSettings");
        }
    }
}
