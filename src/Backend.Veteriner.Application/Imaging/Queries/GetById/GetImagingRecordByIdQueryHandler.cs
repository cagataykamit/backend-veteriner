using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Application.Files.Specs;
using Backend.Veteriner.Application.Imaging.Access;
using Backend.Veteriner.Application.Imaging.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imaging.Queries.GetById;

public sealed class GetImagingRecordByIdQueryHandler
    : IRequestHandler<GetImagingRecordByIdQuery, Result<ImagingRecordDetailDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<ImagingRecord> _records;
    private readonly IReadRepository<StoredFile> _files;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;

    public GetImagingRecordByIdQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<ImagingRecord> records,
        IReadRepository<StoredFile> files,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _records = records;
        _files = files;
        _pets = pets;
        _clients = clients;
    }

    public async ValueTask<Result<ImagingRecordDetailDto>> Handle(GetImagingRecordByIdQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<ImagingRecordDetailDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var row = await ImagingRecordReadScope.FindReadableAsync(
            _records, _clinicContext, _clinicScopeResolver, tenantId, request.Id, ct);
        if (row is null)
        {
            return Result<ImagingRecordDetailDto>.Failure(
                ImagingRecordReadScope.NotFoundCode,
                ImagingRecordReadScope.NotFoundMessage);
        }

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, row.PetId), ct);
        var clientId = pet?.ClientId ?? Guid.Empty;
        var client = clientId != Guid.Empty
            ? await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, clientId), ct)
            : null;

        var files = await _files.ListAsync(
            new StoredFilesReadyByOwnerSpec(tenantId, StoredFileOwnerType.ImagingRecord, row.Id), ct);

        return Result<ImagingRecordDetailDto>.Success(new ImagingRecordDetailDto(
            row.Id,
            row.ClinicId,
            row.PetId,
            pet?.Name ?? string.Empty,
            clientId,
            client?.FullName ?? string.Empty,
            row.ExaminationId,
            row.PerformedAtUtc,
            row.Modality,
            row.Description,
            row.ClinicalNote,
            row.Status,
            row.AddedAfterExaminationFinalization,
            row.CreatedAtUtc,
            row.CreatedByUserId,
            row.UpdatedAtUtc,
            row.CancelledAtUtc,
            row.CancelledByUserId,
            row.CancelReason,
            ExaminationRowVersion.Encode(row.RowVersion),
            files.Select(StoredFileDto.From).ToList()));
    }
}
