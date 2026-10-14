using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports;

internal static class ImportBatchMappings
{
    public static ImportBatchDto ToDto(
        this ImportBatch b,
        IReadOnlyList<ImportTableInfoDto>? tables = null,
        IReadOnlyList<ImportWarningDto>? warnings = null)
        => new(
            b.Id,
            b.Status,
            b.Name,
            b.ClinicId,
            b.ValidationId,
            b.CreatedByUserId,
            b.CreatedAtUtc,
            b.UpdatedAtUtc,
            b.ValidatedAtUtc,
            b.CommittedAtUtc,
            b.RolledBackAtUtc,
            b.RemindersReleasedAtUtc,
            b.PayloadPurgedAtUtc,
            new ImportBatchSummaryDto(b.CustomerCounts.ToDto(), b.PetCounts.ToDto(), b.VaccinationCounts.ToDto()),
            tables,
            warnings);

    private static ImportTableCountsDto ToDto(this ImportTableCounts c)
        => new(
            c.Total, c.Valid, c.Warning, c.Error,
            c.WillCreate, c.WillMatch, c.WillSkip,
            c.Created, c.Matched, c.Skipped, c.Failed);
}
