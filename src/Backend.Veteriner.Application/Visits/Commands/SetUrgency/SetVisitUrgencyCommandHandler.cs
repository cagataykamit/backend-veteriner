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

namespace Backend.Veteriner.Application.Visits.Commands.SetUrgency;

public sealed class SetVisitUrgencyCommandHandler : IRequestHandler<SetVisitUrgencyCommand, Result<VisitDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Visit> _visitsRead;
    private readonly IRepository<Visit> _visitsWrite;
    private readonly IClinicVeterinarianReader _veterinarians;

    public SetVisitUrgencyCommandHandler(
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

    public async Task<Result<VisitDto>> Handle(SetVisitUrgencyCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VisitDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
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

        var sequenceBefore = visit.MutationSequence;
        var change = visit.SetUrgent(request.IsUrgent);
        if (!change.IsSuccess)
            return Result<VisitDto>.Failure(change.Error);

        // Değer zaten istenen ise (tekrar istek) değişiklik yok.
        if (visit.MutationSequence == sequenceBefore)
            return Result<VisitDto>.Success(await visit.ToDtoAsync(_veterinarians, ct));

        try
        {
            await _visitsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı çift istek: istenen değer başkası tarafından sağlandıysa başarı, aksi halde tekrar denenmeli.
            var current = await _visitsRead.FirstOrDefaultAsync(
                new VisitByIdSpec(tenantId, request.VisitId, asNoTracking: true), ct);
            if (current is { IsVoided: false } && current.IsUrgent == request.IsUrgent)
                return Result<VisitDto>.Success(await current.ToDtoAsync(_veterinarians, ct));

            return Result<VisitDto>.Failure(
                "Visits.ConcurrencyConflict",
                "Geliş kaydı eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }

        return Result<VisitDto>.Success(await visit.ToDtoAsync(_veterinarians, ct));
    }
}
