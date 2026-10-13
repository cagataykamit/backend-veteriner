using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabResultInterpretationRequired : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mevcut kayitlar false alir: eski veride yorum gerekmiyor sayilir (rozet yalniz yeni kayitlarda).
            migrationBuilder.AddColumn<bool>(
                name: "InterpretationRequired",
                table: "LabResults",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InterpretationRequired",
                table: "LabResults");
        }
    }
}
