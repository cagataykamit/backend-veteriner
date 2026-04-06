using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Veteriner.Infrastructure.Migrations
{
    /// <summary>
    /// Tablo eklendikten önce oluşmuş kiracılar için Basic + Trialing trial satırı (trial 14 gün, tenant CreatedAtUtc referans).
    /// </summary>
    public partial class BackfillTenantSubscriptionsForExistingTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            const int trialDays = 14;

            migrationBuilder.Sql($"""
                INSERT INTO TenantSubscriptions (
                    TenantId,
                    PlanCode,
                    Status,
                    TrialStartsAtUtc,
                    TrialEndsAtUtc,
                    ActivatedAtUtc,
                    CancelledAtUtc,
                    CreatedAtUtc,
                    UpdatedAtUtc)
                SELECT
                    t.Id,
                    0,
                    0,
                    t.CreatedAtUtc,
                    DATEADD(day, {trialDays}, t.CreatedAtUtc),
                    NULL,
                    NULL,
                    t.CreatedAtUtc,
                    NULL
                FROM Tenants t
                WHERE NOT EXISTS (
                    SELECT 1 FROM TenantSubscriptions s WHERE s.TenantId = t.Id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE s
                FROM TenantSubscriptions s
                INNER JOIN Tenants t ON t.Id = s.TenantId
                WHERE s.PlanCode = 0
                  AND s.Status = 0
                  AND s.TrialStartsAtUtc = t.CreatedAtUtc
                  AND s.TrialEndsAtUtc = DATEADD(day, 14, t.CreatedAtUtc)
                  AND s.ActivatedAtUtc IS NULL
                  AND s.CancelledAtUtc IS NULL
                  AND s.CreatedAtUtc = t.CreatedAtUtc
                  AND s.UpdatedAtUtc IS NULL;
                """);
        }
    }
}
