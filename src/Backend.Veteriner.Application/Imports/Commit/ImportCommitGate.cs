using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Commit;

/// <summary>
/// Önizlemedeki <c>blockers</c>/<c>canCommit</c> ile commit önkoşulunun tek yeri (IMPORT_API_CONTRACT.md §7.6, §7.9):
/// kullanıcı "uygula" düğmesini gördüğü kuralla commit edilir.
/// </summary>
public static class ImportCommitGate
{
    /// <summary>Dosya düzeyi engeller (eşleme + tablo varlığı); satır verisi gerektirmez.</summary>
    public static List<string> FileBlockers(ImportBatch batch)
        => ImportPreconditions.Check(
                ImportMappingDocument.Parse(batch.MappingJson), ImportTableInfoLoader.PresentTables(batch), batch.ClinicId is not null)
            .Select(r => r.Code)
            .ToList();

    /// <summary>Dosya düzeyi + satırı olan bloklayan eşlenmemiş değerler + hiç aktarılabilir satır yok.</summary>
    public static List<string> Blockers(ImportBatch batch, IReadOnlyList<ImportUnmappedValueDto> unmapped)
    {
        var blockers = FileBlockers(batch);
        blockers.AddRange(ImportPreviewAggregator.BlockingCodes(unmapped));

        var accepted = batch.SumCounts(c => c.Valid + c.Warning);
        var total = batch.SumCounts(c => c.Total);
        if (blockers.Count == 0 && accepted == 0 && total > 0)
            blockers.Add(ImportRules.NoImportableRows.Code);

        return blockers;
    }

    public static int WillCreate(ImportBatch batch) => batch.SumCounts(c => c.WillCreate);

    public static int ErrorRows(ImportBatch batch) => batch.SumCounts(c => c.Error);

    public static bool CanCommit(ImportBatch batch, IReadOnlyCollection<string> blockers)
        => batch.Status == ImportBatchStatus.Validated
           && batch.ValidationId is not null
           && blockers.Count == 0
           && WillCreate(batch) > 0;
}
