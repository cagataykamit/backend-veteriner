using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Access;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Imports.Commands.UpdateMapping;

/// <summary>
/// Eşlemeyi doğrular ve kaydeder (IMPORT_API_CONTRACT.md §7.3). Hiçbir şey kısmen kaydedilmez: ilk hatada
/// partiye dokunulmaz. Tür uyumu gerektiren denetimler satır bağlamı ister ve doğrulama (B3) adımındadır.
/// </summary>
public sealed class UpdateImportBatchMappingCommandHandler
    : IRequestHandler<UpdateImportBatchMappingCommand, Result<ImportBatchDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IRepository<ImportBatch> _batches;
    private readonly ImportTableInfoLoader _tableInfo;
    private readonly ImportCatalogLoader _catalogs;
    private readonly TimeProvider _timeProvider;

    public UpdateImportBatchMappingCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IRepository<ImportBatch> batches,
        ImportTableInfoLoader tableInfo,
        ImportCatalogLoader catalogs,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _batches = batches;
        _tableInfo = tableInfo;
        _catalogs = catalogs;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ImportBatchDto>> Handle(UpdateImportBatchMappingCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Fail(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.Id, tracking: true), ct);
        if (batch is null || !await ImportBatchVisibility.IsVisibleAsync(batch, _clinicContext, _clinicScopeResolver, ct))
            return Result<ImportBatchDto>.Failure(ImportBatchVisibility.NotFound());

        // Durum denetimi içerik denetiminden önce: silinmiş/uygulanmış partide eksik sütun hatası yanıltıcı olurdu.
        if (!batch.CanMap)
            return Fail("Imports.InvalidState", $"Parti '{batch.Status}' durumundayken eşleme değiştirilemez.");

        var tables = await _tableInfo.LoadAsync(batch, ct);
        var body = request.Mapping;

        var columns = ValidateColumns(body.Columns, tables);
        if (!columns.IsSuccess)
            return Result<ImportBatchDto>.Failure(columns.Error);

        var valueMappings = await ValidateValueMappingsAsync(tenantId, body.ValueMappings, ct);
        if (!valueMappings.IsSuccess)
            return Result<ImportBatchDto>.Failure(valueMappings.Error);

        var document = new ImportMappingDocument
        {
            Columns = columns.Value!,
            ValueMappings = valueMappings.Value!,
        };
        var options = new ImportOptionsDocument(body.Options?.OnDuplicateClient ?? ImportOptionsDocument.Match);

        var applied = batch.ApplyMapping(document.ToJson(), options.ToJson(), _timeProvider.GetUtcNow().UtcDateTime);
        if (!applied.IsSuccess)
            return Result<ImportBatchDto>.Failure(applied.Error);

        try
        {
            await _batches.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(
                "Imports.ConcurrencyConflict",
                "Parti eşzamanlı olarak değişti; durumu yeniden okuyup tekrar deneyin.");
        }

        var headers = new Dictionary<ImportTable, IReadOnlyList<string>>();
        foreach (var info in tables)
        {
            if (ImportCanonicalNames.TryParseKey(info.Table, out var table))
                headers[table] = info.Columns.Select(c => c.Source).ToList();
        }

        return Result<ImportBatchDto>.Success(batch.ToDto(ImportTableInfoLoader.Build(batch, headers)));
    }

    /// <summary>Tablo → (kanonik alan → kaynak sütun); partideki her tablo zorunlu alanlarını karşılamalıdır.</summary>
    private static Result<Dictionary<string, Dictionary<string, string>>> ValidateColumns(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? requested,
        IReadOnlyList<ImportTableInfoDto> tables)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var present = tables.Select(t => t.Table).ToHashSet(StringComparer.Ordinal);

        foreach (var key in requested?.Keys ?? [])
        {
            if (!present.Contains(key))
            {
                return FailColumns(
                    "Imports.MappingUnknownColumn",
                    $"'{key}' tablosu bu partide yok (tablolar: {string.Join(", ", present)}).");
            }
        }

        var incomplete = new List<string>();
        foreach (var info in tables)
        {
            ImportCanonicalNames.TryParseKey(info.Table, out var table);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var sources = info.Columns.Select(c => c.Source).ToHashSet(StringComparer.Ordinal);

            if (requested is not null && requested.TryGetValue(info.Table, out var entries))
            {
                foreach (var (field, source) in entries)
                {
                    // Boş değer eşlemeyi kaldırır (alan eşlenmemiş sayılır).
                    if (string.IsNullOrWhiteSpace(source))
                        continue;

                    if (!ImportCanonicalSchema.IsCanonical(table, field))
                    {
                        return FailColumns(
                            "Imports.MappingUnknownColumn",
                            $"'{field}' {ImportCanonicalNames.Display(table)} tablosunda tanımlı bir alan değil.");
                    }

                    if (!sources.Contains(source))
                    {
                        return FailColumns(
                            "Imports.MappingUnknownColumn",
                            $"'{source}' sütunu {ImportCanonicalNames.Display(table)} dosyasında yok.");
                    }

                    if (map.ContainsValue(source))
                    {
                        return FailColumns(
                            "Imports.MappingSourceReused",
                            $"'{source}' sütunu {ImportCanonicalNames.Display(table)} tablosunda birden fazla alana eşlenemez.");
                    }

                    map[field] = source;
                }
            }

            var missing = ImportCanonicalSchema.MissingRequired(table, map.Keys);
            if (missing.Count > 0)
                incomplete.Add($"{ImportCanonicalNames.Display(table)}: {string.Join(", ", missing)}");

            result[info.Table] = map;
        }

        if (incomplete.Count > 0)
            return FailColumns("Imports.MappingIncomplete", $"Eksik zorunlu alan eşlemesi — {string.Join("; ", incomplete)}.");

        return Result<Dictionary<string, Dictionary<string, string>>>.Success(result);
    }

    private async Task<Result<List<ImportValueMapping>>> ValidateValueMappingsAsync(
        Guid tenantId,
        IReadOnlyList<ImportValueMappingRequest>? requested,
        CancellationToken ct)
    {
        var result = new List<ImportValueMapping>();
        if (requested is null or { Count: 0 })
            return Result<List<ImportValueMapping>>.Success(result);

        ImportCatalogs? catalogs = null;
        var seen = new HashSet<(ImportValueKind, string)>();
        foreach (var item in requested)
        {
            // Biçim (kind, source) FluentValidation'da denetlendi.
            ImportValueKinds.TryParse(item.Kind, out var kind);
            var source = item.Source!.Trim();

            if (!seen.Add((kind, ImportTextFolding.Fold(source))))
                return FailValues($"'{source}' değeri {ImportValueKinds.Key(kind)} için birden fazla kez eşlenmiş.");

            if (item.TargetId is { } targetId)
            {
                catalogs ??= await _catalogs.LoadAsync(tenantId, ct);
                if (!catalogs.Of(kind).Any(e => e.Id == targetId))
                {
                    return FailValues(
                        $"'{source}' için seçilen {ImportValueKinds.Key(kind)} hedefi yok, pasif veya bu kiracıya görünür değil.");
                }
            }

            result.Add(new ImportValueMapping(ImportValueKinds.Key(kind), source, item.TargetId));
        }

        return Result<List<ImportValueMapping>>.Success(result);
    }

    private static Result<Dictionary<string, Dictionary<string, string>>> FailColumns(string code, string message)
        => Result<Dictionary<string, Dictionary<string, string>>>.Failure(code, message);

    private static Result<List<ImportValueMapping>> FailValues(string message)
        => Result<List<ImportValueMapping>>.Failure("Imports.ValueMappingTargetInvalid", message);

    private static Result<ImportBatchDto> Fail(string code, string message)
        => Result<ImportBatchDto>.Failure(code, message);
}
