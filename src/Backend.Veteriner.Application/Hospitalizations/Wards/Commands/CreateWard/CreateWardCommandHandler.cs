using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Wards.Access;
using Backend.Veteriner.Application.Hospitalizations.Wards.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateWard;

public sealed class CreateWardCommandHandler : IRequestHandler<CreateWardCommand, Result<Guid>>
{
    private readonly IHospitalizationWardClinicAccess _access;
    private readonly IReadRepository<HospitalizationWard> _wardsRead;
    private readonly IRepository<HospitalizationWard> _wardsWrite;
    private readonly TimeProvider _timeProvider;

    public CreateWardCommandHandler(
        IHospitalizationWardClinicAccess access,
        IReadRepository<HospitalizationWard> wardsRead,
        IRepository<HospitalizationWard> wardsWrite,
        TimeProvider timeProvider)
    {
        _access = access;
        _wardsRead = wardsRead;
        _wardsWrite = wardsWrite;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateWardCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<Guid>.Failure(access.Error);
        var tenantId = access.Value;

        var duplicate = await _wardsRead.AnyAsync(
            new ActiveHospitalizationWardByClinicAndNameSpec(
                tenantId, request.ClinicId, HospitalizationWardMapping.NormalizeNameForLookup(request.Name)),
            ct);
        if (duplicate)
        {
            return Result<Guid>.Failure(
                "HospitalizationWards.DuplicateName",
                "Bu klinikte aynı adda aktif bir bölüm zaten var.");
        }

        HospitalizationWard ward;
        try
        {
            ward = HospitalizationWard.Create(
                tenantId, request.ClinicId, request.Name, request.SortOrder, _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure("HospitalizationWards.Validation", ex.Message);
        }

        await _wardsWrite.AddAsync(ward, ct);
        await _wardsWrite.SaveChangesAsync(ct);
        return Result<Guid>.Success(ward.Id);
    }
}
