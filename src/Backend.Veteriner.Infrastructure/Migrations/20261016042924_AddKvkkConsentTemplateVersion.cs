using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKvkkConsentTemplateVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TextHash",
                table: "ClinicKvkkFormTemplates",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TextVersion",
                table: "ClinicKvkkFormTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "KvkkConsentTemplateHash",
                table: "Clients",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KvkkConsentTemplateVersion",
                table: "Clients",
                type: "int",
                nullable: true);

            // ADR-032: kliniğin önceden kaydettiği şablon metni sürüm 1 sayılır; özet, ClinicKvkkFormTemplate.ComputeTextHash
            // ile aynıdır (metnin UTF-8 baytlarının SHA-256'sı, küçük harf hex). UTF-8 harmanlaması SQL Server 2019+ gerektirir.
            // Müşteri rıza kayıtları (Clients) geriye dönük doldurulmaz: null = sürüm bilinmiyor.
            migrationBuilder.Sql(
                """
                UPDATE [ClinicKvkkFormTemplates]
                SET [TextVersion] = 1,
                    [TextHash] = LOWER(CONVERT(char(64), HASHBYTES('SHA2_256',
                        CAST([Text] COLLATE Latin1_General_100_CI_AS_SC_UTF8 AS varchar(max))), 2));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TextHash",
                table: "ClinicKvkkFormTemplates");

            migrationBuilder.DropColumn(
                name: "TextVersion",
                table: "ClinicKvkkFormTemplates");

            migrationBuilder.DropColumn(
                name: "KvkkConsentTemplateHash",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "KvkkConsentTemplateVersion",
                table: "Clients");
        }
    }
}
