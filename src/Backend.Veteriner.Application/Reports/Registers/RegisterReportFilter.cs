using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>Ham defter sorgusu (İstanbul takvim günleri, ikisi dahil); <see cref="RegisterReportRunner"/> doğrular ve çözer.</summary>
public sealed record RegisterQuery(DateOnly From, DateOnly To, Guid? ClinicId, bool IncludeVoided);

/// <summary>Doğrulanmış ve klinik kapsamı çözülmüş süzgeç; JSON ve dışa aktarma tek yerden okur.</summary>
internal sealed record RegisterReportFilter(
    Guid TenantId,
    Guid? ClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    DateTime FromUtc,
    DateTime ToUtcExclusive,
    bool IncludeVoided)
{
    public RegisterReadRequest ToReadRequest(int skip, int take)
        => new(TenantId, ClinicId, AccessibleClinicIds, FromUtc, ToUtcExclusive, IncludeVoided, skip, take);

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

        if (query.ClinicId.HasValue && clinicContext.ClinicId.HasValue
                                    && query.ClinicId.Value != clinicContext.ClinicId.Value)
        {
            return Result<RegisterReportFilter>.Failure(
                "Registers.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
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
            query.IncludeVoided));
    }
}
