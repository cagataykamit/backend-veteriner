using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ImportedRecords.Commands;

/// <summary>
/// İçe aktarma hesap hareketi yolu (ADR-048 T4, IMPORT_API_CONTRACT.md §12.5): müşteriye salt okunur eski program hareketi
/// yazar. Bakiyeyi, kasayı, raporları ve e-belgeyi etkilemez (ücret/ödeme kaydı oluşmaz). Yalnız içe aktarma uygulama
/// işinden çağrılır; transaction sınırı çağıran sahip birimidir.
/// </summary>
public sealed record CreateImportedAccountEntryCommand(
    Guid ImportBatchId,
    Guid ClinicId,
    Guid ClientId,
    DateOnly OccurredOn,
    ImportedAccountEntryKind Kind,
    decimal Amount,
    string? Description,
    string? PaymentMethod,
    string? DocumentNumber,
    string? PetName)
    : IRequest<Result<Guid>>;

public sealed class CreateImportedAccountEntryCommandHandler
    : IRequestHandler<CreateImportedAccountEntryCommand, Result<Guid>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Client> _clients;
    private readonly IRepository<ImportedAccountEntry> _entries;
    private readonly TimeProvider _timeProvider;

    public CreateImportedAccountEntryCommandHandler(
        ITenantContext tenantContext,
        IReadRepository<Clinic> clinics,
        IReadRepository<Client> clients,
        IRepository<ImportedAccountEntry> entries,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinics = clinics;
        _clients = clients;
        _entries = entries;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<Guid>> Handle(CreateImportedAccountEntryCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<Guid>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        if (await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct) is null)
            return Result<Guid>.Failure("Clinics.NotFound", "Klinik bulunamadı veya kiracıya ait değil.");

        if (await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, request.ClientId), ct) is null)
            return Result<Guid>.Failure("Clients.NotFound", "Müşteri bulunamadı veya kiracıya ait değil.");

        var created = ImportedAccountEntry.Create(
            tenantId,
            request.ClinicId,
            request.ClientId,
            request.ImportBatchId,
            request.OccurredOn,
            request.Kind,
            request.Amount,
            request.Description,
            request.PaymentMethod,
            request.DocumentNumber,
            request.PetName,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<Guid>.Failure(created.Error);

        await _entries.AddAsync(created.Value!, ct);
        await _entries.SaveChangesAsync(ct);
        return Result<Guid>.Success(created.Value!.Id);
    }
}
