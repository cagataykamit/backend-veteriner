using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <summary>
    /// Eski telefon normalizasyonu (yalnızca rakamlar) ile kaydedilmiş 10/11 haneli değerleri 905XXXXXXXXX biçimine çevirir.
    /// Uymayan veya çok uzun kayıtlar değiştirilmez (manuel temizlik gerekebilir).
    /// </summary>
    public partial class BackfillClientPhoneTurkishE164 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 10 hane, 5 ile başlayan (eski strip): 5XXXXXXXXX -> 905XXXXXXXXX
            migrationBuilder.Sql("""
                UPDATE Clients
                SET PhoneNormalized = CONCAT('90', PhoneNormalized),
                    Phone = CONCAT('90', PhoneNormalized)
                WHERE PhoneNormalized IS NOT NULL
                  AND LEN(PhoneNormalized) = 10
                  AND LEFT(PhoneNormalized, 1) = '5';
                """);

            // 11 hane 05XXXXXXXXX -> 90 + 10 hane
            migrationBuilder.Sql("""
                UPDATE Clients
                SET PhoneNormalized = CONCAT('90', SUBSTRING(PhoneNormalized, 2, 10)),
                    Phone = CONCAT('90', SUBSTRING(PhoneNormalized, 2, 10))
                WHERE PhoneNormalized IS NOT NULL
                  AND LEN(PhoneNormalized) = 11
                  AND LEFT(PhoneNormalized, 2) = '05';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alınamaz; veri dönüşümü tek yönlü.
        }
    }
}
