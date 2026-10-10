using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Visits.Commands.Correct;

public sealed class CorrectVisitCommandHandler : IRequestHandler<CorrectVisitCommand, Result<VisitDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Visit> _visitsRead;
    private readonly IRepository<Visit> _visitsWrite;
    private readonly IReadRepository<Examination> _examinations;
    private readonly TimeProvider _timeProvider;

    public CorrectVisitCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Visit> visitsRead,
        IRepository<Visit> visitsWrite,
        IReadRepository<Examination> examinations,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _visitsRead = visitsRead;
        _visitsWrite = visitsWrite;
        _examinations = examinations;
        _timeProvider = timeProvider;
    }

    public async Task<Result<VisitDto>> Handle(CorrectVisitCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VisitDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (request.TargetCareStatus.HasValue == request.MarkAsMistaken)
        {
            return Result<VisitDto>.Failure(
                "Visits.Validation",
                "Hedef bakım durumu veya yanlış geliş işareti tam olarak biri verilmelidir.");
        }

        var visit = await _visitsRead.FirstOrDefaultAsync(new VisitByIdSpec(tenantId, request.VisitId), ct);
        if (visit is null || visit.IsVoided
            || (_clinicContext.ClinicId is { } contextClinicId && visit.ClinicId != contextClinicId))
        {
            return Result<VisitDto>.Failure("Visits.NotFound", "Geliş kaydı bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, visit.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<VisitDto>.Failure(clinicAccess.Error);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        Result correction;

        if (request.MarkAsMistaken)
        {
            if (await _examinations.AnyAsync(new ExaminationsByVisitIdSpec(tenantId, visit.Id), ct))
            {
                return Result<VisitDto>.Failure(
                    "Visits.HasExaminations",
                    "Muayene kaydı bulunan geliş yanlış geliş olarak işaretlenemez.");
            }

            correction = visit.MarkAsMistaken(request.Reason, nowUtc);
        }
        else
        {
            var target = request.TargetCareStatus!.Value;

            // Tamamlanmış gelişi geri alırken hayvanın başka aktif gelişi varsa ikinci aktif geliş oluşur.
            if (visit.CareStatus == VisitCareStatus.Completed && target != VisitCareStatus.Completed
                && await HasOtherActiveVisitAsync(tenantId, visit, ct))
            {
                return DuplicateActiveVisit();
            }

            correction = visit.CorrectCareStatus(target, request.Reason, nowUtc);
        }

        if (!correction.IsSuccess)
            return Result<VisitDto>.Failure(correction.Error);


        try
        {
            await _visitsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<VisitDto>.Failure(
                "Visits.ConcurrencyConflict",
                "Geliş kaydı eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }
        catch (DbUpdateException) when (request.TargetCareStatus.HasValue)
        {
            // Kontrol ile kayıt arasında başka aktif geliş açıldı: filtreli benzersiz indeks korur.
            return DuplicateActiveVisit();
        }

        return Result<VisitDto>.Success(visit.ToDto());
    }

    private async Task<bool> HasOtherActiveVisitAsync(Guid tenantId, Visit visit, CancellationToken ct)
    {
        var active = await _visitsRead.FirstOrDefaultAsync(new ActiveVisitByPetIdSpec(tenantId, visit.PetId), ct);
        return active is not null && active.Id != visit.Id;
    }

    private static Result<VisitDto> DuplicateActiveVisit()
        => Result<VisitDto>.Failure(
            "Visits.DuplicateActiveVisit",
            "Hayvanın başka bir aktif gelişi var; önce onu tamamlayın veya düzeltin.");
}
