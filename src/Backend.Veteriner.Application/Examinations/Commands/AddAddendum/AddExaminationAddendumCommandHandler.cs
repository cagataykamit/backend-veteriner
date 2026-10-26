using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.Examinations.Contracts.Dtos;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using Mediator;

namespace Backend.Veteriner.Application.Examinations.Commands.AddAddendum;

public sealed class AddExaminationAddendumCommandHandler
    : IRequestHandler<AddExaminationAddendumCommand, Result<ExaminationAddendumDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IRepository<ExaminationAddendum> _addenda;
    private readonly IClinicVeterinarianReader _userNames;
    private readonly TimeProvider _timeProvider;

    public AddExaminationAddendumCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClientContext clientContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Examination> examinations,
        IRepository<ExaminationAddendum> addenda,
        IClinicVeterinarianReader userNames,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clientContext = clientContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _examinations = examinations;
        _addenda = addenda;
        _userNames = userNames;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ExaminationAddendumDto>> Handle(AddExaminationAddendumCommand request, CancellationToken ct)
    {
        if (_clientContext.UserId is not { } userId)
        {
            return Result<ExaminationAddendumDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var loaded = await ExaminationWriteAccess.LoadAsync(
            _tenantContext, _clinicContext, _clinicScopeResolver, _tenants, _examinations, request.ExaminationId, ct);
        if (!loaded.IsSuccess)
            return Result<ExaminationAddendumDto>.Failure(loaded.Error);

        var created = loaded.Value!.AddAddendum(userId, request.Reason, request.Text, _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<ExaminationAddendumDto>.Failure(created.Error);

        var addendum = created.Value!;
        await _addenda.AddAsync(addendum, ct);
        await _addenda.SaveChangesAsync(ct);

        var names = await _userNames.GetNamesAsync([userId], ct);
        names.TryGetValue(userId, out var name);
        return Result<ExaminationAddendumDto>.Success(ExaminationAddendumDto.From(addendum, name));
    }
}
