using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.LabResults.Queries.GetParameterHistory;

public sealed class GetLabParameterHistoryQueryHandler
    : IRequestHandler<GetLabParameterHistoryQuery, Result<IReadOnlyList<LabParameterHistoryPointDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<LabResult> _labResults;

    public GetLabParameterHistoryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<LabResult> labResults)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _labResults = labResults;
    }

    public async ValueTask<Result<IReadOnlyList<LabParameterHistoryPointDto>>> Handle(
        GetLabParameterHistoryQuery request,
        CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (ClinicContextRule.Conflicts(request.ClinicId, _clinicContext.ClinicId))
            return Result<IReadOnlyList<LabParameterHistoryPointDto>>.Failure(ClinicContextRule.Mismatch("LabResults"));

        // Liste ucuyla aynı güvenlik kuralı: klinik kapsamı olmadan okunmaz.
        var requestedClinicId = request.ClinicId ?? _clinicContext.ClinicId;
        if (requestedClinicId is null)
        {
            return Fail(
                "LabResults.ClinicScopeRequired",
                "Klinik kapsamı gerekli: aktif klinik bağlamı yok ve clinicId belirtilmedi.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scope.IsSuccess)
            return Result<IReadOnlyList<LabParameterHistoryPointDto>>.Failure(scope.Error);

        var rows = await _labResults.ListAsync(
            new LabResultsWithParameterLinesSpec(
                tenantId,
                request.PetId,
                scope.Value!.SingleClinicId,
                scope.Value.AccessibleClinicIds,
                [TurkishTextKey.Normalize(request.Parameter)]),
            ct);

        IReadOnlyList<LabParameterHistoryPointDto> points = rows
            .SelectMany(r => r.Lines.Select(l => new LabParameterHistoryPointDto(
                r.Id,
                r.ResultDateUtc!.Value,
                r.TestName,
                l.ParameterName,
                l.Value,
                l.Unit,
                l.ReferenceLow,
                l.ReferenceHigh,
                l.Flag)))
            .ToList();

        return Result<IReadOnlyList<LabParameterHistoryPointDto>>.Success(points);
    }

    private static Result<IReadOnlyList<LabParameterHistoryPointDto>> Fail(string code, string message)
        => Result<IReadOnlyList<LabParameterHistoryPointDto>>.Failure(code, message);
}
