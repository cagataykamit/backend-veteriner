using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Visits.Commands.SetPreparation;

public sealed class SetVisitPreparationCommandHandler : IRequestHandler<SetVisitPreparationCommand, Result<VisitDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Visit> _visitsRead;
    private readonly IRepository<Visit> _visitsWrite;
    private readonly IReadRepository<ExaminationTemplate> _templates;
    private readonly IClinicVeterinarianReader _veterinarians;

    public SetVisitPreparationCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Visit> visitsRead,
        IRepository<Visit> visitsWrite,
        IReadRepository<ExaminationTemplate> templates,
        IClinicVeterinarianReader veterinarians)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _visitsRead = visitsRead;
        _visitsWrite = visitsWrite;
        _templates = templates;
        _veterinarians = veterinarians;
    }

    public async ValueTask<Result<VisitDto>> Handle(SetVisitPreparationCommand request, CancellationToken ct)
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

        // Yalnız yeni seçilen şablon denetlenir: pasifleşmiş ama zaten hazırlanmış şablon, hazırlık metni düzeltilirken kalabilir.
        if (request.PreparedExaminationTemplateId != visit.PreparedExaminationTemplateId)
        {
            var template = await VisitPreparationTemplateRule.EnsureSelectableAsync(
                _templates, tenantId, request.PreparedExaminationTemplateId, ct);
            if (!template.IsSuccess)
                return Result<VisitDto>.Failure(template.Error);
        }

        var sequenceBefore = visit.MutationSequence;
        var change = visit.SetPreparation(request.ArrivalReason, request.PreparedExaminationTemplateId);
        if (!change.IsSuccess)
            return Result<VisitDto>.Failure(change.Error);

        if (visit.MutationSequence == sequenceBefore)
            return Result<VisitDto>.Success(await visit.ToDtoAsync(_veterinarians, ct));

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

        return Result<VisitDto>.Success(await visit.ToDtoAsync(_veterinarians, ct));
    }
}
