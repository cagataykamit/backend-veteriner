using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.Medications;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>İlaç kayıt defteri: ilaç ürünlerinin (aşılar ve diğer ürünler hariç) stok girişleri (sözleşme bölüm 14).</summary>
public sealed class MedicationRegisterReader : IRegisterReader<MedicationRegisterItemDto>
{
    private readonly AppDbContext _db;

    public MedicationRegisterReader(AppDbContext db)
        => _db = db;

    public async Task<RegisterReadResult<MedicationRegisterItemDto>> ReadAsync(
        RegisterReadRequest request,
        CancellationToken ct = default)
    {
        var (totalCount, rows) = await StockEntryRegisterSource.ReadAsync(_db, request, ProductKind.Medicine, ct);

        var items = rows.Select((r, i) => new MedicationRegisterItemDto(
            request.Skip + i + 1,
            r.MovementId,
            r.ProductId,
            r.ProductName,
            r.PharmacologicalForm,
            r.PresentationForm,
            r.Quantity,
            r.Unit,
            r.SupplierName,
            r.ReceivedDate ?? OperationDayBounds.ToLocalDate(r.OccurredAtUtc),
            r.InvoiceDate,
            r.InvoiceNumber,
            r.LotNumber,
            r.IsBatched,
            r.IsReversed)).ToList();

        return new RegisterReadResult<MedicationRegisterItemDto>(totalCount, items);
    }
}
