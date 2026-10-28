using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Hbs;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>Ham defter sorgusu (İstanbul takvim günleri, ikisi dahil); <see cref="RegisterReportRunner"/> doğrular ve çözer.</summary>
/// <param name="ProductId">Yalnız Aşı Bilgi Raporu: ürün süzgeci.</param>
/// <param name="VeterinarianUserId">Aşı Bilgi Raporu ve HBS takip listeleri: geliş kaydındaki sorumlu hekim süzgeci.</param>
/// <param name="HbsStatus">Yalnız HBS takip listeleri; verilmezse "Girilmedi" (HBS_ASSISTANT_API_CONTRACT.md bölüm 6).</param>
public sealed record RegisterQuery(
    DateOnly From,
    DateOnly To,
    Guid? ClinicId,
    bool IncludeVoided,
    Guid? ProductId = null,
    Guid? VeterinarianUserId = null,
    HbsEntryStatus? HbsStatus = null);

/// <summary>Doğrulanmış ve klinik kapsamı çözülmüş süzgeç; JSON ve dışa aktarma tek yerden okur.</summary>
internal sealed record RegisterReportFilter(
    Guid TenantId,
    Guid? ClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    DateTime FromUtc,
    DateTime ToUtcExclusive,
    bool IncludeVoided,
    Guid? ProductId,
    Guid? VeterinarianUserId,
    HbsEntryStatus? HbsStatus)
{
    public RegisterReadRequest ToReadRequest(int skip, int take)
        => new(TenantId, ClinicId, AccessibleClinicIds, FromUtc, ToUtcExclusive, IncludeVoided, ProductId, VeterinarianUserId, skip, take, HbsStatus);

    public static async Task<Result<RegisterReportFilter>> ResolveAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        RegisterQuery query,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<RegisterReportFilter>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (query.From == default || query.To == default)
        {
            return Result<RegisterReportFilter>.Failure(
                "Registers.DateRangeInvalid",
                "Defter için from ve to zorunludur (yyyy-MM-dd).");
        }

        if (query.From > query.To)
        {
            return Result<RegisterReportFilter>.Failure(
                "Registers.DateRangeInvalid",
                "from değeri to değerinden sonra olamaz.");
        }

        if (query.To.DayNumber - query.From.DayNumber > RegisterReportConstants.MaxRangeDays)
        {
            return Result<RegisterReportFilter>.Failure(
                "Registers.RangeTooLong",
                $"Tarih aralığı en fazla {RegisterReportConstants.MaxRangeDays} gün olabilir.");
        }

        if (ClinicContextRule.Conflicts(query.ClinicId, clinicContext.ClinicId))
        {
            return Result<RegisterReportFilter>.Failure(ClinicContextRule.Mismatch("Registers"));
        }

        var scopeResult = await scopeResolver.ResolveAsync(tenantId, query.ClinicId ?? clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<RegisterReportFilter>.Failure(scopeResult.Error);

        var scope = scopeResult.Value!;
        return Result<RegisterReportFilter>.Success(new RegisterReportFilter(
            tenantId,
            scope.SingleClinicId,
            scope.AccessibleClinicIds,
            OperationDayBounds.ForLocalDate(query.From).DayStartUtc,
            OperationDayBounds.ForLocalDate(query.To).DayEndUtc,
            query.IncludeVoided,
            query.ProductId,
            query.VeterinarianUserId,
            query.HbsStatus));
    }
}
