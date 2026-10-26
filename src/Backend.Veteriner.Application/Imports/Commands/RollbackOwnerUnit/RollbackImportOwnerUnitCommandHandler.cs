using Backend.Veteriner.Application.Clients.IntegrationEvents;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Commands.ApplyOwnerUnit;
using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Commands.RollbackOwnerUnit;

/// <summary>
/// Birimin bağımlılıklarını bu işlemde yeniden okur, önizlemeyle aynı kuralla (<see cref="ImportRollbackPlanner"/>) karar
/// verir ve aşı → hasta → müşteri sırasıyla siler. Silinen müşteri/hasta için silme olayı (<c>client.deleted.v1</c> /
/// <c>pet.deleted.v1</c>) aynı işlemde outbox'a yazılır. Satır sonuçları ve kira yenilemesi aynı işlemdedir: kira başka
/// işçiye geçtiyse rowversion çakışması birimi geri aldırır. Ürün kodunda başka silme yolu yoktur (§9).
/// </summary>
public sealed class RollbackImportOwnerUnitCommandHandler
    : IRequestHandler<RollbackImportOwnerUnitCommand, Result<ImportRollbackUnitResult>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ImportBatch> _batches;
    private readonly IRepository<ImportBatchRow> _rows;
    private readonly IRepository<Vaccination> _vaccinations;
    private readonly IRepository<Pet> _pets;
    private readonly IRepository<Client> _clients;
    private readonly ImportRollbackPlanLoader _plans;
    private readonly IClientIntegrationEventOutbox _clientEvents;
    private readonly IPetIntegrationEventOutbox _petEvents;
    private readonly TimeProvider _timeProvider;

    public RollbackImportOwnerUnitCommandHandler(
        ITenantContext tenantContext,
        IRepository<ImportBatch> batches,
        IRepository<ImportBatchRow> rows,
        IRepository<Vaccination> vaccinations,
        IRepository<Pet> pets,
        IRepository<Client> clients,
        ImportRollbackPlanLoader plans,
        IClientIntegrationEventOutbox clientEvents,
        IPetIntegrationEventOutbox petEvents,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _batches = batches;
        _rows = rows;
        _vaccinations = vaccinations;
        _pets = pets;
        _clients = clients;
        _plans = plans;
        _clientEvents = clientEvents;
        _petEvents = petEvents;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ImportRollbackUnitResult>> Handle(RollbackImportOwnerUnitCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ImportRollbackUnitResult>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.BatchId, tracking: true), ct);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (batch is null || !batch.RenewRollbackLease(request.LeaseOwner, now).IsSuccess)
            return Result<ImportRollbackUnitResult>.Failure(ImportOwnerUnitErrors.LeaseLost());

        var rows = await _rows.ListAsync(new ImportRollbackTargetRowsSpec(batch.Id, request.RowIds), ct);
        if (rows.Count == 0)
            return Result<ImportRollbackUnitResult>.Success(new ImportRollbackUnitResult(AlreadyDone: true, 0, 0));

        // Denetim silmeyle aynı işlemde: önizlemeden/istekten sonra açılan bağ burada görülür.
        var plan = (await _plans.LoadAsync(tenantId, batch.Id, rows.Select(r => r.Id).ToArray(), ct)).Plan;
        if (request.Policy == ImportRollbackPolicy.Fail)
            plan = ImportRollbackPlanner.KeepWholeUnitIfBlocked(plan);

        var items = plan.Items.ToDictionary(i => i.Row.RowId);
        var payloadKept = batch.PayloadPurgedAtUtc is null;
        foreach (var row in rows)
        {
            var item = items[row.Id];
            var status = item.Decision == ImportRollbackDecision.Keep ? ImportRowApplyStatus.Kept : ImportRowApplyStatus.RolledBack;
            row.RecordRollbackResult(status, payloadKept ? RollbackIssues(row.IssuesJson, item) : row.IssuesJson);
        }

        var deleted = await DeleteAsync(tenantId, plan, now, ct);

        // Satır sonuçları + kira yenilemesi (silme kayıtlarıyla aynı işlem); parti devralındıysa rowversion çakışır.
        await _rows.SaveChangesAsync(ct);
        return Result<ImportRollbackUnitResult>.Success(
            new ImportRollbackUnitResult(AlreadyDone: false, deleted, plan.Total(ImportRollbackDecision.Keep)));
    }

    /// <summary>Sıra aşı → hasta → müşteri; müşteri/hasta silme olayı silmeyle aynı işlemde.</summary>
    private async Task<int> DeleteAsync(Guid tenantId, ImportRollbackPlan plan, DateTime now, CancellationToken ct)
    {
        Guid[] ToDelete(ImportTable table) => plan.Items
            .Where(i => i.Row.Table == table && i.Decision == ImportRollbackDecision.Delete)
            .Select(i => i.TargetId)
            .ToArray();

        var deleted = 0;
        var vaccinationIds = ToDelete(ImportTable.Vaccinations);
        if (vaccinationIds.Length > 0)
        {
            var vaccinations = await _vaccinations.ListAsync(new ImportRollbackVaccinationsSpec(tenantId, vaccinationIds), ct);
            await _vaccinations.DeleteRangeAsync(vaccinations, ct);
            deleted += vaccinations.Count;
        }

        var petIds = ToDelete(ImportTable.Pets);
        if (petIds.Length > 0)
        {
            var pets = await _pets.ListAsync(new ImportRollbackPetsSpec(tenantId, petIds), ct);
            foreach (var pet in pets)
            {
                await _petEvents.EnqueueAsync(
                    PetIntegrationEventTypes.Deleted,
                    new PetDeletedIntegrationEvent(Guid.NewGuid(), now, pet.Id, pet.TenantId),
                    ct);
            }

            await _pets.DeleteRangeAsync(pets, ct);
            deleted += pets.Count;
        }

        var clientIds = ToDelete(ImportTable.Customers);
        if (clientIds.Length > 0)
        {
            var clients = await _clients.ListAsync(new ImportRollbackClientsSpec(tenantId, clientIds), ct);
            foreach (var client in clients)
            {
                await _clientEvents.EnqueueAsync(
                    ClientIntegrationEventTypes.Deleted,
                    new ClientDeletedIntegrationEvent(Guid.NewGuid(), now, client.Id, client.TenantId),
                    ct);
            }

            await _clients.DeleteRangeAsync(clients, ct);
            deleted += clients.Count;
        }

        return deleted;
    }

    /// <summary>Önceki geri alma nedeni çıkarılır; kalan satıra güncel neden (<c>Imports.RollbackKept</c>) eklenir.</summary>
    private static string? RollbackIssues(string? issuesJson, ImportRollbackItem item)
    {
        var issues = ImportIssueJson.Parse(issuesJson).Where(i => i.Code != ImportRules.RollbackKept.Code).ToList();
        if (item.Decision == ImportRollbackDecision.Keep)
            issues.Add(ImportRules.RollbackKept.At(value: string.Join(",", item.Reasons)));
        return ImportIssueJson.Serialize(issues);
    }
}
