using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations;

/// <summary>
/// Global Species / Breeds tabloları; Pets.Species (serbest metin) → Pets.SpeciesId (FK).
/// Mevcut satırlar, eski metin alanından olası eşleşmelere göre doldurulur; kalanlar OTHER.
/// </summary>
public partial class AddCatalogSpeciesBreedsAndPetSpeciesFk : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Species",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                DisplayOrder = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Species", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Breeds",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SpeciesId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Breeds", x => x.Id);
                table.ForeignKey(
                    name: "FK_Breeds_Species_SpeciesId",
                    column: x => x.SpeciesId,
                    principalTable: "Species",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Species_Code",
            table: "Species",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Breeds_SpeciesId",
            table: "Breeds",
            column: "SpeciesId");

        migrationBuilder.CreateIndex(
            name: "IX_Breeds_SpeciesId_Name",
            table: "Breeds",
            columns: new[] { "SpeciesId", "Name" },
            unique: true);

        SeedSpeciesRows(migrationBuilder);

        migrationBuilder.AddColumn<Guid>(
            name: "SpeciesId",
            table: "Pets",
            type: "uniqueidentifier",
            nullable: true);

        BackfillPetSpeciesIds(migrationBuilder);

        migrationBuilder.Sql($"""
            UPDATE Pets SET SpeciesId = '{SpeciesSeedConstants.Other}'
            WHERE SpeciesId IS NULL;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "SpeciesId",
            table: "Pets",
            type: "uniqueidentifier",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uniqueidentifier",
            oldNullable: true);

        migrationBuilder.DropColumn(
            name: "Species",
            table: "Pets");

        migrationBuilder.CreateIndex(
            name: "IX_Pets_SpeciesId",
            table: "Pets",
            column: "SpeciesId");

        migrationBuilder.AddForeignKey(
            name: "FK_Pets_Species_SpeciesId",
            table: "Pets",
            column: "SpeciesId",
            principalTable: "Species",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    private static void SeedSpeciesRows(MigrationBuilder migrationBuilder)
    {
        void Row(Guid id, string code, string name, int order) =>
            migrationBuilder.InsertData(
                table: "Species",
                columns: new[] { "Id", "Code", "Name", "IsActive", "DisplayOrder" },
                values: new object[] { id, code, name, true, order });

        Row(SpeciesSeedConstants.Cat, "CAT", "Kedi", 10);
        Row(SpeciesSeedConstants.Dog, "DOG", "Köpek", 20);
        Row(SpeciesSeedConstants.Bird, "BIRD", "Kuş", 30);
        Row(SpeciesSeedConstants.Rabbit, "RABBIT", "Tavşan", 40);
        Row(SpeciesSeedConstants.Hamster, "HAMSTER", "Hamster", 50);
        Row(SpeciesSeedConstants.Turtle, "TURTLE", "Kaplumbağa", 60);
        Row(SpeciesSeedConstants.Fish, "FISH", "Balık", 70);
        Row(SpeciesSeedConstants.Reptile, "REPTILE", "Sürüngen", 80);
        Row(SpeciesSeedConstants.Other, "OTHER", "Diğer", 999);
    }

    /// <summary>
    /// Eski serbest metin → bilinen kodlar; tam eşleşme + yaygın TR/EN varyantları.
    /// Agresif tahmin yerine güvenli liste; kalanlar OTHER.
    /// </summary>
    private static void BackfillPetSpeciesIds(MigrationBuilder migrationBuilder)
    {
        static string G(Guid g) => g.ToString();

        var cat = G(SpeciesSeedConstants.Cat);
        var dog = G(SpeciesSeedConstants.Dog);
        var bird = G(SpeciesSeedConstants.Bird);
        var rabbit = G(SpeciesSeedConstants.Rabbit);
        var hamster = G(SpeciesSeedConstants.Hamster);
        var turtle = G(SpeciesSeedConstants.Turtle);
        var fish = G(SpeciesSeedConstants.Fish);
        var reptile = G(SpeciesSeedConstants.Reptile);
        var other = G(SpeciesSeedConstants.Other);

        // Kod kolonu (büyük harf) ve yaygın isimler (case-insensitive Latin)
        migrationBuilder.Sql($"""
            UPDATE Pets SET SpeciesId = '{cat}'
            WHERE SpeciesId IS NULL AND (
                UPPER(LTRIM(RTRIM(Species))) = N'CAT'
                OR LOWER(LTRIM(RTRIM(Species))) IN (N'kedi', N'cat', N'cats')
            );
            UPDATE Pets SET SpeciesId = '{dog}'
            WHERE SpeciesId IS NULL AND (
                UPPER(LTRIM(RTRIM(Species))) = N'DOG'
                OR LOWER(LTRIM(RTRIM(Species))) IN (N'köpek', N'kopek', N'dog', N'dogs')
            );
            UPDATE Pets SET SpeciesId = '{bird}'
            WHERE SpeciesId IS NULL AND (
                UPPER(LTRIM(RTRIM(Species))) = N'BIRD'
                OR LOWER(LTRIM(RTRIM(Species))) IN (N'kuş', N'kus', N'bird', N'birds')
            );
            UPDATE Pets SET SpeciesId = '{rabbit}'
            WHERE SpeciesId IS NULL AND (
                UPPER(LTRIM(RTRIM(Species))) = N'RABBIT'
                OR LOWER(LTRIM(RTRIM(Species))) IN (N'tavşan', N'tavsan', N'rabbit')
            );
            UPDATE Pets SET SpeciesId = '{hamster}'
            WHERE SpeciesId IS NULL AND (
                UPPER(LTRIM(RTRIM(Species))) = N'HAMSTER'
                OR LOWER(LTRIM(RTRIM(Species))) IN (N'hamster')
            );
            UPDATE Pets SET SpeciesId = '{turtle}'
            WHERE SpeciesId IS NULL AND (
                UPPER(LTRIM(RTRIM(Species))) = N'TURTLE'
                OR LOWER(LTRIM(RTRIM(Species))) IN (N'kaplumbağa', N'kaplumbaga', N'turtle')
            );
            UPDATE Pets SET SpeciesId = '{fish}'
            WHERE SpeciesId IS NULL AND (
                UPPER(LTRIM(RTRIM(Species))) = N'FISH'
                OR LOWER(LTRIM(RTRIM(Species))) IN (N'balık', N'balik', N'fish')
            );
            UPDATE Pets SET SpeciesId = '{reptile}'
            WHERE SpeciesId IS NULL AND (
                UPPER(LTRIM(RTRIM(Species))) = N'REPTILE'
                OR LOWER(LTRIM(RTRIM(Species))) IN (N'sürüngen', N'surungen', N'reptile')
            );
            UPDATE Pets SET SpeciesId = '{other}'
            WHERE SpeciesId IS NULL AND (
                UPPER(LTRIM(RTRIM(Species))) = N'OTHER'
                OR LOWER(LTRIM(RTRIM(Species))) IN (N'diğer', N'diger', N'other')
            );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Pets_Species_SpeciesId",
            table: "Pets");

        migrationBuilder.DropIndex(
            name: "IX_Pets_SpeciesId",
            table: "Pets");

        migrationBuilder.DropColumn(
            name: "SpeciesId",
            table: "Pets");

        migrationBuilder.AddColumn<string>(
            name: "Species",
            table: "Pets",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.DropTable(
            name: "Breeds");

        migrationBuilder.DropTable(
            name: "Species");
    }
}
