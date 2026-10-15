using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicKvkkDisclosureFormTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClinicKvkkFormTemplates_TenantId_ClinicId",
                table: "ClinicKvkkFormTemplates");

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ClinicKvkkFormTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicKvkkFormTemplates_TenantId_ClinicId_Kind",
                table: "ClinicKvkkFormTemplates",
                columns: new[] { "TenantId", "ClinicId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClinicKvkkFormTemplates_TenantId_ClinicId_Kind",
                table: "ClinicKvkkFormTemplates");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ClinicKvkkFormTemplates");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicKvkkFormTemplates_TenantId_ClinicId",
                table: "ClinicKvkkFormTemplates",
                columns: new[] { "TenantId", "ClinicId" },
                unique: true);
        }
    }
}
