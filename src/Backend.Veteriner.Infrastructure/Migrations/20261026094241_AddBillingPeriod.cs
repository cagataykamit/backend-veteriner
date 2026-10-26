using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BillingPeriod",
                table: "TenantSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "BillingPeriod",
                table: "BillingCheckoutSessions",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingPeriod",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "BillingPeriod",
                table: "BillingCheckoutSessions");
        }
    }
}
