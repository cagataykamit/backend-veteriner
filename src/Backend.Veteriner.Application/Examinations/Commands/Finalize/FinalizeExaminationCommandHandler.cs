using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Examinations.Commands.Finalize;

public sealed class FinalizeExaminationCommandHandler
    : IRequestHandler<FinalizeExaminationCommand, Result<ExaminationFinalizeResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Examination> _examinationsRead;
    private readonly IRepository<Examination> _examinationsWrite;
    private readonly TimeProvider _timeProvider;

    public FinalizeExaminationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Examination> examinationsRead,
        IRepository<Examination> examinationsWrite,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _examinationsRead = examinationsRead;
        _examinationsWrite = examinationsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ExaminationFinalizeResultDto>> Handle(FinalizeExaminationCommand request, CancellationToken ct)
    {
        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
        {
            return Result<ExaminationFinalizeResultDto>.Failure(
                "Examinations.Validation",
                "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<ExaminationFinalizeResultDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var loaded = await ExaminationWriteAccess.LoadAsync(
            _tenantContext, _clinicContext, _clinicScopeResolver, _tenants, _examinationsRead, request.Id, ct);
        if (!loaded.IsSuccess)
            return Result<ExaminationFinalizeResultDto>.Failure(loaded.Error);

        var e = loaded.Value!;
        var finalized = e.Finalize(userId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!finalized.IsSuccess)
            return Result<ExaminationFinalizeResultDto>.Failure(finalized.Error);

        e.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _examinationsWrite.UpdateAsync(e, ct);
            await _examinationsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ExaminationFinalizeResultDto>.Failure(
                "Examinations.ConcurrencyConflict",
                "Muayene kaydı eşzamanlı olarak güncellendi; kaydı yenileyip tekrar deneyin.");
        }

        return Result<ExaminationFinalizeResultDto>.Success(new ExaminationFinalizeResultDto(
            e.Id,
            ExaminationRowVersion.Encode(e.RowVersion),
            e.Status,
            e.FinalizedAtUtc!.Value,
            e.FinalizedByUserId!.Value));
    }
}
