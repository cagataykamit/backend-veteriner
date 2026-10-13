using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Application.Reports.Registers.ControlledSubstances;

/// <summary>
/// Hareketin defter tarafını ve Alındığı/Çıkış Yapılan Yer, Tarih, Belge No hücrelerini belirleyen kuralların tek yeri
/// (sözleşme bölüm 15). Saf işlev: veritabanına dokunmaz.
/// </summary>
public static class ControlledSubstanceRegisterMapper
{
    private const string Handover = "Devir";
    private const string StockIssue = "Stok çıkışı";
    private const string UndeterminedAdjustment = "Sayım düzeltmesi (yön belirlenemedi)";

    public static ControlledSubstanceRegisterItemDto ToItem(int rowNumber, ControlledSubstanceMovement m)
    {
        var side = Resolve(m, OperationDayBounds.ToLocalDate(m.OccurredAtUtc));

        return new ControlledSubstanceRegisterItemDto(
            rowNumber,
            m.MovementId,
            m.ProductId,
            m.MovementType.ToString(),
            side.Direction.ToString(),
            m.ProductName,
            m.PharmacologicalForm,
            JoinPresentation(m.PresentationForm, m.Packaging),
            m.Quantity,
            m.LotNumber,
            side.ReceivedFrom,
            side.ReceivedDate,
            side.ReceivedDocumentNumber,
            side.IssuedTo,
            side.IssuedDate,
            side.IssuedDocumentNumber,
            m.BatchId is not null);
    }

    private static Side Resolve(ControlledSubstanceMovement m, DateOnly movementDay)
    {
        switch (m.MovementType)
        {
            case StockMovementType.Initial:
            case StockMovementType.In:
                return Side.Received(
                    m.SupplierName ?? (m.MovementType == StockMovementType.Initial ? Handover : null),
                    m.ReceivedDate ?? movementDay,
                    m.InvoiceNumber);

            case StockMovementType.Out:
                return Side.Issued(
                    m.ReferenceType == Administration.StockReferenceType && m.AdministrationTarget is not null
                        ? $"Uygulama: {m.AdministrationTarget}"
                        : m.Reason ?? StockIssue,
                    movementDay);

            case StockMovementType.Reversal:
                var reversal = $"Geri alma: {m.Reason}";
                return m.ReversedMovementType == StockMovementType.Out
                    ? Side.Received(reversal, movementDay, null)
                    : Side.Issued(reversal, movementDay);

            default: // Adjustment: yön saklanmaz; partili ise önceki parti bakiyesinden türetilir.
                if (m.BatchBalanceAfter is { } after && m.PreviousBatchBalance is { } before && after != before)
                {
                    return after > before
                        ? Side.Received("Sayım düzeltmesi (artış)", movementDay, null)
                        : Side.Issued("Sayım düzeltmesi (azalış)", movementDay);
                }

                return Side.Both(UndeterminedAdjustment, movementDay);
        }
    }

    private static string? JoinPresentation(string? presentationForm, string? packaging)
    {
        var parts = new[] { presentationForm, packaging }.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private sealed record Side(
        ControlledSubstanceDirection Direction,
        string? ReceivedFrom,
        DateOnly? ReceivedDate,
        string? ReceivedDocumentNumber,
        string? IssuedTo,
        DateOnly? IssuedDate,
        string? IssuedDocumentNumber)
    {
        public static Side Received(string? place, DateOnly day, string? document)
            => new(ControlledSubstanceDirection.Received, place, day, document, null, null, null);

        public static Side Issued(string? place, DateOnly day)
            => new(ControlledSubstanceDirection.Issued, null, null, null, place, day, null);

        public static Side Both(string place, DateOnly day)
            => new(ControlledSubstanceDirection.Undetermined, place, day, null, place, day, null);
    }
}
