using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Application.Reports.Registers.Vaccines;
using Backend.Veteriner.Domain.Products;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>Aşı kayıt defteri: aşı ürünlerinin stok girişleri (sözleşme bölüm 13).</summary>
public sealed class VaccineRegisterReader : IRegisterReader<VaccineRegisterItemDto>
{
    private readonly AppDbContext _db;

    public VaccineRegisterReader(AppDbContext db)
        => _db = db;

    public async Task<RegisterReadResult<VaccineRegisterItemDto>> ReadAsync(
        RegisterReadRequest request,
        CancellationToken ct = default)
    {
        var (totalCount, rows) = await StockEntryRegisterSource.ReadAsync(_db, request, ProductKind.Vaccine, ct);

        var items = rows.Select((r, i) => new VaccineRegisterItemDto(
            request.Skip + i + 1,
            r.MovementId,
            r.ProductId,
            r.ProductName,
            r.LotNumber,
            r.ExpiryDate,
            r.Quantity,
            r.SupplierName,
            r.TransportMethod,
            r.ColdChainMonitor,
            r.DispatchedDate,
            r.ReceivedDate ?? OperationDayBounds.ToLocalDate(r.OccurredAtUtc),
            r.InvoiceDate,
            r.InvoiceNumber,
            r.IsBatched,
            r.IsReversed)).ToList();

        return new RegisterReadResult<VaccineRegisterItemDto>(totalCount, items);
    }
}
