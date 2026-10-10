using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Visits.Commands.Restore;

public sealed class RestoreVisitCommandHandler : IRequestHandler<RestoreVisitCommand, Result<VisitDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Visit> _visitsRead;
    private readonly IRepository<Visit> _visitsWrite;
    private readonly IClinicVeterinarianReader _veterinarians;

    public RestoreVisitCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Visit> visitsRead,
        IRepository<Visit> visitsWrite,
        IClinicVeterinarianReader veterinarians)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _visitsRead = visitsRead;
        _visitsWrite = visitsWrite;
        _veterinarians = veterinarians;
    }

    public async Task<Result<VisitDto>> Handle(RestoreVisitCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VisitDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var visit = await _visitsRead.FirstOrDefaultAsync(new VisitByIdSpec(tenantId, request.VisitId), ct);
        if (visit is null
            || (_clinicContext.ClinicId is { } contextClinicId && visit.ClinicId != contextClinicId))
        {
            return Result<VisitDto>.Failure("Visits.NotFound", "Geliş kaydı bulunamadı.");
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, visit.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<VisitDto>.Failure(clinicAccess.Error);

        var restore = visit.RestoreFromMistaken(request.Reason);
        if (!restore.IsSuccess)
            return Result<VisitDto>.Failure(restore.Error);

        if (await FindConflictAsync(tenantId, visit, ct) is { } conflict)
            return conflict;

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
        catch (DbUpdateException)
        {
            // Kontrol ile kayıt arasında aynı hayvan/randevu için geliş açıldı: filtreli benzersiz indeks korur.
            if (await FindConflictAsync(tenantId, visit, ct) is { } raced)
                return raced;

            throw;
        }

        return Result<VisitDto>.Success(await visit.ToDtoAsync(_veterinarians, ct));
    }

    /// <summary>Geri alınan kayıt, filtreli benzersiz indeks kurallarından (hayvan başına aktif, randevu başına tek) birini bozuyor mu?</summary>
    private async Task<Result<VisitDto>?> FindConflictAsync(Guid tenantId, Visit visit, CancellationToken ct)
    {
        if (visit.CareStatus != VisitCareStatus.Completed
            && await _visitsRead.FirstOrDefaultAsync(new ActiveVisitByPetIdSpec(tenantId, visit.PetId), ct) is not null)
        {
            return Result<VisitDto>.Failure(
                "Visits.DuplicateActiveVisit",
                "Hayvanın başka bir aktif gelişi var; önce onu tamamlayın veya düzeltin.");
        }

        if (visit.AppointmentId is { } appointmentId
            && await _visitsRead.FirstOrDefaultAsync(new VisitByAppointmentIdSpec(tenantId, appointmentId), ct) is not null)
        {
            return Result<VisitDto>.Failure(
                "Visits.DuplicateAppointmentVisit",
                "Bu randevu için başka bir geliş kaydı var.");
        }

        return null;
    }
}
