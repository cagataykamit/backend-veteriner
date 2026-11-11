using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.EDocuments;

/// <summary>
/// Var olan belge üzerindeki eylemlerin (yeniden dene, vazgeç, iptal, yenile) ortak akışı: belgeyi ve bağlantıyı izlenen olarak
/// yükler, belgenin kliniğine erişimi denetler, eylemi çalıştırır, eşzamanlılık çakışmasını anlaşılır hataya çevirir.
/// </summary>
public sealed class EDocumentActionRunner
{
    private readonly EDocumentAccess _access;
    private readonly IRepository<EDocument> _documents;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly IReadRepository<Client> _clients;
    private readonly IEDocumentProviderResolver _providers;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public EDocumentActionRunner(
        EDocumentAccess access,
        IRepository<EDocument> documents,
        IRepository<EDocumentConnection> connections,
        IReadRepository<Client> clients,
        IEDocumentProviderResolver providers,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _access = access;
        _documents = documents;
        _connections = connections;
        _clients = clients;
        _providers = providers;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public delegate Task<Result> DocumentAction(EDocument document, EDocumentConnection? connection, EDocumentCaller caller, CancellationToken ct);

    public async Task<Result<EDocumentDto>> RunAsync(Guid documentId, string permission, DocumentAction action, CancellationToken ct)
    {
        var basics = _access.Basics(permission);
        if (!basics.IsSuccess)
            return Result<EDocumentDto>.Failure(basics.Error);

        var document = await _documents.FirstOrDefaultAsync(new EDocumentByIdSpec(basics.Value!.TenantId, documentId, tracked: true), ct);
        if (document is null || document.Status == EDocumentStatus.Draft)
            return Result<EDocumentDto>.Failure(EDocumentErrors.NotFound());

        var access = await _access.ResolveForClinicAsync(permission, document.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentDto>.Failure(access.Error);

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(document.TenantId, document.ClinicId, tracked: true), ct);

        var outcome = await action(document, connection, access.Value!, ct);
        if (!outcome.IsSuccess)
        {
            // Eylem reddedildi; sağlayıcı çağrısında yenilenmiş belirteç varsa yine de saklanır.
            await SaveQuietlyAsync(ct);
            return Result<EDocumentDto>.Failure(outcome.Error);
        }

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<EDocumentDto>.Failure(EDocumentErrors.ConcurrencyConflict());
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(document.TenantId, document.ClientId), ct);
        return Result<EDocumentDto>.Success(document.ToDto(client?.FullName ?? string.Empty, _providers, _timeProvider.GetUtcNow().UtcDateTime));
    }

    private async Task SaveQuietlyAsync(CancellationToken ct)
    {
        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Başka bir işlem belgeyi güncelledi; reddedilen eylemin yazacağı bir şey yok.
        }
    }
}
