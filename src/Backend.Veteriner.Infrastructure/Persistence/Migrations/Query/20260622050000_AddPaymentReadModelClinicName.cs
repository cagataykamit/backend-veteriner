using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Persistence.Migrations.Query
{
    /// <inheritdoc />
    public partial class AddPaymentReadModelClinicName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CQRS-15D: PaymentReadModels enrichment. Non-null kolon; mevcut satırlar için boş string default,
            // backfill-payment-read-models ile Command DB truth'tan gerçek klinik adı doldurulur.
            migrationBuilder.AddColumn<string>(
                name: "ClinicName",
                table: "PaymentReadModels",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClinicName",
                table: "PaymentReadModels");
        }
    }
}
