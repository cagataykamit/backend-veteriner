using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ImportedRecords.Commands;

/// <summary>
/// İçe aktarma klinik geçmiş yolu (ADR-048 T1, IMPORT_API_CONTRACT.md §12.5): hastaya salt okunur eski program kaydı yazar.
/// Yalnız içe aktarma uygulama işinden çağrılır (HTTP ucu yoktur); izinler ve klinik erişimi commit isteğinde
/// denetlenmiştir. Muayene, reçete, lab ve defter kaydı oluşmaz. Kayıt sahibi hastanın bugünkü müşterisidir. Transaction
/// sınırı çağıran sahip birimidir.
/// </summary>
public sealed record CreateImportedClinicalRecordCommand(
    Guid ImportBatchId,
    Guid ClinicId,
    Guid PetId,
    DateOnly RecordedOn,
    ImportedClinicalRecord.Content Content)
    : IRequest<Result<Guid>>;

public sealed class CreateImportedClinicalRecordCommandHandler
    : IRequestHandler<CreateImportedClinicalRecordCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IRepository<ImportedClinicalRecord> _records;
    private readonly TimeProvider _timeProvider;

    public CreateImportedClinicalRecordCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IRepository<ImportedClinicalRecord> records,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinics = clinics;
        _pets = pets;
        _records = records;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateImportedClinicalRecordCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<Guid>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        if (await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct) is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, request.PetId), ct);
        if (pet is null)
            return Result<Guid>.Failure("Pets.NotFound", "Hayvan kaydı bulunamadı veya kiracıya ait değil.");

        var created = ImportedClinicalRecord.Create(
            tenantId,
            request.ClinicId,
            pet.Id,
            pet.ClientId,
            request.ImportBatchId,
            request.RecordedOn,
            request.Content,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<Guid>.Failure(created.Error);

        await _records.AddAsync(created.Value!, ct);
        await _records.SaveChangesAsync(ct);
        return Result<Guid>.Success(created.Value!.Id);
    }
}
