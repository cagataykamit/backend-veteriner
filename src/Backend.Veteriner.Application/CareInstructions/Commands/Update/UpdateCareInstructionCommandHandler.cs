using Backend.Veteriner.Application.CareInstructions.Contracts.Dtos;
using Backend.Veteriner.Application.CareInstructions.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.CareInstructions.Commands.Update;

public sealed class UpdateCareInstructionCommandHandler
    : IRequestHandler<UpdateCareInstructionCommand, Result<CareInstructionWriteResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IRepository<CareInstruction> _careInstructionsWrite;
    private readonly TimeProvider _timeProvider;

    public UpdateCareInstructionCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IRepository<CareInstruction> careInstructionsWrite,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _careInstructionsWrite = careInstructionsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CareInstructionWriteResultDto>> Handle(UpdateCareInstructionCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
            return Fail("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct);
        if (tenant is null)
            return Fail("Tenants.NotFound", "Tenant bulunamadı.");

        if (!tenant.IsActive)
            return Fail("Tenants.TenantInactive", "Pasif kiracı için bakım talimatı güncellenemez.");

        var existing = await _careInstructionsWrite.FirstOrDefaultAsync(
            new CareInstructionForUpdateByIdSpec(tenantId, request.Id), ct);
        if (existing is null)
            return Fail("CareInstructions.NotFound", "Bakım talimatı bulunamadı.");

        if (_clinicContext.ClinicId is { } ctxClinicId && existing.ClinicId != ctxClinicId)
            return Fail("CareInstructions.NotFound", "Bakım talimatı bulunamadı.");

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, existing.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<CareInstructionWriteResultDto>.Failure(scope.Error);

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
            return Fail("CareInstructions.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var update = existing.UpdateText(userId, request.Text, utcNow);
        if (!update.IsSuccess)
            return Result<CareInstructionWriteResultDto>.Failure(update.Error);

        existing.SetExpectedRowVersion(expectedRowVersion);

        try
        {
            await _careInstructionsWrite.UpdateAsync(existing, ct);
            await _careInstructionsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(
                "CareInstructions.ConcurrencyConflict",
                "Bakım talimatı eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin.");
        }

        return Result<CareInstructionWriteResultDto>.Success(
            new CareInstructionWriteResultDto(existing.Id, ExaminationRowVersion.Encode(existing.RowVersion)));
    }

    private static Result<CareInstructionWriteResultDto> Fail(string code, string message)
        => Result<CareInstructionWriteResultDto>.Failure(code, message);
}
