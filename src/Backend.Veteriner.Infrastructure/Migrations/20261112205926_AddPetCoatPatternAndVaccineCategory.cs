using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPetCoatPatternAndVaccineCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "VaccineDefinitions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PatternId",
                table: "Pets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "PetColors",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020001"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020002"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020003"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020004"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020005"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020006"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020007"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020008"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020009"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020010"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020011"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020012"),
                columns: new[] { "DisplayOrder", "Kind" },
                values: new object[] { 103, 2 });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020013"),
                columns: new[] { "DisplayOrder", "Kind" },
                values: new object[] { 104, 2 });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020014"),
                columns: new[] { "DisplayOrder", "Kind" },
                values: new object[] { 105, 2 });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020015"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020016"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020017"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020018"),
                columns: new[] { "DisplayOrder", "Kind", "Name" },
                values: new object[] { 102, 2, "Kaplan Çizgili (Brindle)" });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020019"),
                columns: new[] { "IsActive", "Kind", "Name" },
                values: new object[] { false, 1, "Kaplan Çizgili-Beyaz" });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020020"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020021"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020022"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020023"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020024"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020025"),
                columns: new[] { "Kind", "Name" },
                values: new object[] { 1, "Mavi (Seyreltik Siyah)" });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020026"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020027"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020028"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020029"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020030"),
                columns: new[] { "IsActive", "Kind", "Name" },
                values: new object[] { false, 1, "Kahverengi Kaplan Çizgili" });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020031"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020032"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020033"),
                column: "Kind",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020034"),
                columns: new[] { "DisplayOrder", "Kind" },
                values: new object[] { 106, 2 });

            migrationBuilder.InsertData(
                table: "PetColors",
                columns: new[] { "Id", "Code", "DisplayOrder", "IsActive", "Kind", "Name" },
                values: new object[,]
                {
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020035"), "SMOKE", 37, true, 1, "Dumanlı (Smoke)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020036"), "SILVER", 38, true, 1, "Gümüş (Silver)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020037"), "LILAC", 39, true, 1, "Lila (Lilac)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020038"), "CINNAMON", 40, true, 1, "Tarçın (Cinnamon)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020039"), "FAWN", 41, true, 1, "Fawn (Açık Sarı-Kahve)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020040"), "BLACK_TAN", 42, true, 1, "Siyah-Bakır (Black and Tan)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020041"), "SABLE", 43, true, 1, "Samur (Sable)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020042"), "TRICOLOR", 44, true, 1, "Siyah-Beyaz-Kahve (Tricolor)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020043"), "HORSE_BAY", 60, true, 1, "Doru (At)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020044"), "HORSE_CHESTNUT", 61, true, 1, "Al (At)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020045"), "HORSE_GREY", 62, true, 1, "Kır (At)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020046"), "HORSE_BLACK", 63, true, 1, "Yağız (At)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020047"), "HORSE_DUN", 64, true, 1, "Kula (At)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020048"), "HORSE_ISABELLINE", 65, true, 1, "İzabel (At)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020049"), "TABBY", 101, true, 2, "Tekir (Tabby)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020050"), "TORTOISESHELL", 107, true, 2, "Kaplumbağa Kabuğu (Tortoiseshell)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020051"), "COLORPOINT", 108, true, 2, "Sivri Renk (Colorpoint)" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020052"), "VAN", 109, true, 2, "Van Deseni" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020053"), "MERLE", 110, true, 2, "Merle" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020054"), "HARLEQUIN", 111, true, 2, "Harlequin" },
                    { new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020055"), "ROAN", 112, true, 2, "Kırçıl (Roan)" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Pets_PatternId",
                table: "Pets",
                column: "PatternId");

            migrationBuilder.AddForeignKey(
                name: "FK_Pets_PetColors_PatternId",
                table: "Pets",
                column: "PatternId",
                principalTable: "PetColors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Renk ile desen ayrıldı (FIFe EMS): desen olan eski renk seçimleri hastanın desen alanına taşınır; renk + desen
            // birleşimi iki satır (Kırçıllı-Beyaz, Kahverengi-Kırçıllı) renk ve kaplan çizgili desene ayrılır, sonra pasifleşir.
            // Okuma modelindeki renk adı için ardından DbMigrator backfill-pet-projections çalıştırılır.
            migrationBuilder.Sql(@"
UPDATE Pets SET PatternId = ColorId, ColorId = NULL
WHERE ColorId IN ('3d2d8f3a-6b0b-4d1e-9f2a-111111020012', '3d2d8f3a-6b0b-4d1e-9f2a-111111020013', '3d2d8f3a-6b0b-4d1e-9f2a-111111020014',
                  '3d2d8f3a-6b0b-4d1e-9f2a-111111020018', '3d2d8f3a-6b0b-4d1e-9f2a-111111020034');
UPDATE Pets SET PatternId = '3d2d8f3a-6b0b-4d1e-9f2a-111111020018', ColorId = '3d2d8f3a-6b0b-4d1e-9f2a-111111020002'
WHERE ColorId = '3d2d8f3a-6b0b-4d1e-9f2a-111111020019';
UPDATE Pets SET PatternId = '3d2d8f3a-6b0b-4d1e-9f2a-111111020018', ColorId = '3d2d8f3a-6b0b-4d1e-9f2a-111111020003'
WHERE ColorId = '3d2d8f3a-6b0b-4d1e-9f2a-111111020030';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri dönüşte desen, renk alanı boşsa renge geri yazılır (birleşik satır ayrımı geri alınmaz).
            migrationBuilder.Sql("UPDATE Pets SET ColorId = PatternId WHERE ColorId IS NULL AND PatternId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Pets_PetColors_PatternId",
                table: "Pets");

            migrationBuilder.DropIndex(
                name: "IX_Pets_PatternId",
                table: "Pets");

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020035"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020036"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020037"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020038"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020039"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020040"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020041"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020042"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020043"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020044"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020045"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020046"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020047"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020048"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020049"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020050"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020051"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020052"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020053"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020054"));

            migrationBuilder.DeleteData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020055"));

            migrationBuilder.DropColumn(
                name: "Category",
                table: "VaccineDefinitions");

            migrationBuilder.DropColumn(
                name: "PatternId",
                table: "Pets");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PetColors");

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020012"),
                column: "DisplayOrder",
                value: 12);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020013"),
                column: "DisplayOrder",
                value: 13);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020014"),
                column: "DisplayOrder",
                value: 14);

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020018"),
                columns: new[] { "DisplayOrder", "Name" },
                values: new object[] { 20, "Kırçıllı" });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020019"),
                columns: new[] { "IsActive", "Name" },
                values: new object[] { true, "Kırçıllı-Beyaz" });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020025"),
                column: "Name",
                value: "Mavi");

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020030"),
                columns: new[] { "IsActive", "Name" },
                values: new object[] { true, "Kahverengi-Kırçıllı" });

            migrationBuilder.UpdateData(
                table: "PetColors",
                keyColumn: "Id",
                keyValue: new Guid("3d2d8f3a-6b0b-4d1e-9f2a-111111020034"),
                column: "DisplayOrder",
                value: 36);
        }
    }
}
