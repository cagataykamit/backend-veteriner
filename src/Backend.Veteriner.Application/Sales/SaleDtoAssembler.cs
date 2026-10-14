using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ProductStocks.Specs;
using Backend.Veteriner.Application.Administrations;
using Backend.Veteriner.Application.Sales.Contracts.Dtos;
using Backend.Veteriner.Application.StockBatches;
using Backend.Veteriner.Application.StockBatches.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Sales;

namespace Backend.Veteriner.Application.Sales;

/// <summary>
/// Satış yanıtlarının (oluşturma, iptal, detay, liste) ortak okuma tarafı: müşteri adı, ücret tahsis durumu, parti lot/SKT
/// ve ürün stok durumu toplu okunur (satır başına sorgu yok).
/// </summary>
public sealed class SaleDtoAssembler
{
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<StockBatch> _batches;
    private readonly IReadRepository<ProductStock> _stocks;
    private readonly ExpiredStockReader _expiredStock;

    public SaleDtoAssembler(
        IReadRepository<Client> clients,
        IReadRepository<Charge> charges,
        IReadRepository<StockBatch> batches,
        IReadRepository<ProductStock> stocks,
        ExpiredStockReader expiredStock)
    {
        _clients = clients;
        _charges = charges;
        _batches = batches;
        _stocks = stocks;
        _expiredStock = expiredStock;
    }

    public async Task<IReadOnlyList<SaleDto>> AssembleAsync(
        Guid tenantId, IReadOnlyList<Sale> sales, CancellationToken ct)
    {
        if (sales.Count == 0)
            return [];

        var clientIds = sales.Select(s => s.ClientId).Distinct().ToArray();
        var clients = (await _clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, clientIds), ct))
            .ToDictionary(c => c.Id);

        var chargeIds = sales.Where(s => s.ChargeId.HasValue).Select(s => s.ChargeId!.Value).Distinct().ToArray();
        var charges = chargeIds.Length == 0
            ? new Dictionary<Guid, Charge>()
            : (await _charges.ListAsync(new ChargesByIdsSpec(tenantId, chargeIds), ct)).ToDictionary(c => c.Id);

        var batchIds = sales.SelectMany(s => s.Lines).Where(l => l.BatchId.HasValue)
            .Select(l => l.BatchId!.Value).Distinct().ToArray();
        var batches = batchIds.Length == 0
            ? new Dictionary<Guid, StockBatch>()
            : (await _batches.ListAsync(new StockBatchesByIdsSpec(tenantId, batchIds), ct)).ToDictionary(b => b.Id);

        // Müşteri kaydı kiracıya ait olduğundan her satışın müşterisi bulunur; yoksa veri tutarsızdır ve satır atlanmaz, hata fırlar.
        return sales
            .Select(s => s.ToDto(
                clients[s.ClientId],
                s.ChargeId is { } id && charges.TryGetValue(id, out var charge) ? charge : null,
                batches))
            .ToList();
    }

    /// <summary>Satıştaki her ürün için güncel stok durumu (sepet sırasıyla, tekrarsız).</summary>
    public async Task<IReadOnlyList<SaleStockDto>> StocksAsync(Sale sale, CancellationToken ct)
    {
        var result = new List<SaleStockDto>();
        foreach (var productId in sale.Lines.Select(l => l.ProductId).Distinct())
        {
            var stock = await _stocks.FirstOrDefaultAsync(
                new ProductStockForUpdateSpec(sale.TenantId, sale.ClinicId, productId), ct);
            if (stock is null)
                continue;

            var expired = await _expiredStock.GetAsync(sale.TenantId, sale.ClinicId, productId, ct);
            var dto = stock.ToDto(expired);
            result.Add(new SaleStockDto(
                productId,
                dto.QuantityOnHand,
                dto.MinimumStockLevel,
                dto.IsBelowMinimum,
                dto.AvailableQuantity,
                dto.ExpiredQuantity));
        }

        return result;
    }
}
