using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.IntegrationEvents;
using Backend.Veteriner.Application.Visits.Specs;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Visits;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Visits.Commands.Transition;

public sealed class TransitionVisitCommandHandler : IRequestHandler<TransitionVisitCommand, Result<VisitDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Visit> _visitsRead;
    private readonly IRepository<Visit> _visitsWrite;
    private readonly IVisitIntegrationEventOutbox _eventOutbox;
    private readonly TimeProvider _timeProvider;

    public TransitionVisitCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Visit> visitsRead,
        IRepository<Visit> visitsWrite,
        IVisitIntegrationEventOutbox eventOutbox,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _visitsRead = visitsRead;
        _visitsWrite = visitsWrite;
        _eventOutbox = eventOutbox;
        _timeProvider = timeProvider;
    }

    public async Task<Result<VisitDto>> Handle(TransitionVisitCommand request, CancellationToken ct)
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
            return NotFound();
        }

        var clinicAccess = await _clinicScopeResolver.ResolveAsync(tenantId, visit.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return Result<VisitDto>.Failure(clinicAccess.Error);

        var sequenceBefore = visit.MutationSequence;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var transition = request.Target == VisitCareStatus.InProgress
            ? visit.Start(nowUtc)
            : visit.Complete(nowUtc);
        if (!transition.IsSuccess)
            return Result<VisitDto>.Failure(transition.Error);

        // Hedef durumda zaten ise (tekrar istek) değişiklik/olay/yan etki yok.
        if (visit.MutationSequence == sequenceBefore)
            return Result<VisitDto>.Success(visit.ToDto());

        await _eventOutbox.EnqueueUpdatedAsync(visit, ct);

        try
        {
            await _visitsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Eşzamanlı çift istek: hedef durum başkası tarafından sağlandıysa başarı, aksi halde tekrar denenmeli.
            var current = await _visitsRead.FirstOrDefaultAsync(
                new VisitByIdSpec(tenantId, request.VisitId, asNoTracking: true), ct);
            if (current is { IsVoided: false } && current.CareStatus == request.Target)
                return Result<VisitDto>.Success(current.ToDto());

            return Result<VisitDto>.Failure(
                "Visits.ConcurrencyConflict",
                "Geliş kaydı eşzamanlı olarak güncellendi; işlem tekrarlanmalı.");
        }

        return Result<VisitDto>.Success(visit.ToDto());
    }

    private static Result<VisitDto> NotFound()
        => Result<VisitDto>.Failure("Visits.NotFound", "Geliş kaydı bulunamadı.");
}
