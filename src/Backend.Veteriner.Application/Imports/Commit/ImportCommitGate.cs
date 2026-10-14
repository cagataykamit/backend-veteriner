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
        => ImportPreconditions.Check(ImportMappingDocument.Parse(batch.MappingJson), ImportTableInfoLoader.PresentTables(batch))
            .Select(r => r.Code)
            .ToList();

    /// <summary>Dosya düzeyi + satırı olan bloklayan eşlenmemiş değerler + hiç aktarılabilir satır yok.</summary>
    public static List<string> Blockers(ImportBatch batch, IReadOnlyList<ImportUnmappedValueDto> unmapped)
    {
        var blockers = FileBlockers(batch);
        blockers.AddRange(ImportPreviewAggregator.BlockingCodes(unmapped));

        var counts = new[] { batch.CustomerCounts, batch.PetCounts, batch.VaccinationCounts };
        var accepted = counts.Sum(c => c.Valid + c.Warning);
        var total = counts.Sum(c => c.Total);
        if (blockers.Count == 0 && accepted == 0 && total > 0)
            blockers.Add(ImportRules.NoImportableRows.Code);

        return blockers;
    }

    public static int WillCreate(ImportBatch batch)
        => batch.CustomerCounts.WillCreate + batch.PetCounts.WillCreate + batch.VaccinationCounts.WillCreate;

    public static int ErrorRows(ImportBatch batch)
        => batch.CustomerCounts.Error + batch.PetCounts.Error + batch.VaccinationCounts.Error;

    public static bool CanCommit(ImportBatch batch, IReadOnlyCollection<string> blockers)
        => batch.Status == ImportBatchStatus.Validated
           && batch.ValidationId is not null
           && blockers.Count == 0
           && WillCreate(batch) > 0;
}
