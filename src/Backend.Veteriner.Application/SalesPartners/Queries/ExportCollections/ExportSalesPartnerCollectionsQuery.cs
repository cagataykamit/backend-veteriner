using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Csv;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;

namespace Backend.Veteriner.Application.SalesPartners.Queries.ExportCollections;

public sealed record SalesPartnerCollectionsExportDto(byte[] ContentUtf8Bom, string FileDownloadName);

/// <summary>Platform yöneticisi: tahsilat/komisyon satırları CSV (partnerId boşsa tüm ortaklar).</summary>
public sealed record ExportSalesPartnerCollectionsQuery(Guid? SalesPartnerId) : IRequest<Result<SalesPartnerCollectionsExportDto>>;

public sealed class ExportSalesPartnerCollectionsQueryHandler
    : IRequestHandler<ExportSalesPartnerCollectionsQuery, Result<SalesPartnerCollectionsExportDto>>
{
    public const string FileName = "vetinity-satis-ortagi-komisyon.csv";

    private const char Delimiter = ';';
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly string[] Headers =
    [
        "Ortak", "Kod", "Klinik", "Tahsilat tarihi", "Dönem", "Brüt (TL)", "İndirim (TL)", "Net (TL)", "Komisyon (TL)",
        "Durum", "Ödendi tarihi", "Not",
    ];

    private readonly IReadRepository<SalesPartner> _partners;
    private readonly IReadRepository<DiscountRedemption> _redemptions;
    private readonly IReadRepository<Tenant> _tenants;

    public ExportSalesPartnerCollectionsQueryHandler(
        IReadRepository<SalesPartner> partners,
        IReadRepository<DiscountRedemption> redemptions,
        IReadRepository<Tenant> tenants)
    {
        _partners = partners;
        _redemptions = redemptions;
        _tenants = tenants;
    }

    public async Task<Result<SalesPartnerCollectionsExportDto>> Handle(ExportSalesPartnerCollectionsQuery request, CancellationToken ct)
    {
        if (request.SalesPartnerId is { } id && !await _partners.AnyAsync(new SalesPartnerByIdSpec(id), ct))
            return Result<SalesPartnerCollectionsExportDto>.Failure(SalesPartnerErrors.PartnerNotFound);

        var partners = (await _partners.ListAsync(new SalesPartnersOrderedSpec(), ct)).ToDictionary(p => p.Id, p => p.Name);
        var redemptions = await _redemptions.ListAsync(new DiscountRedemptionsByPartnerSpec(request.SalesPartnerId), ct);
        var tenantNames = await SalesPartnerTenantNames.LoadAsync(_tenants, redemptions.Select(r => r.TenantId), ct);

        var sb = new StringBuilder();
        sb.Append('﻿');
        sb.AppendCsvLine(string.Join(Delimiter, Headers));

        var lines = redemptions
            .SelectMany(r => r.Collections.Select(c => (Redemption: r, Collection: c)))
            .OrderBy(x => partners.GetValueOrDefault(x.Redemption.SalesPartnerId), StringComparer.Create(Turkish, true))
            .ThenBy(x => x.Collection.CollectedAtUtc);

        foreach (var (r, c) in lines)
        {
            AppendRow(sb,
            [
                partners.GetValueOrDefault(r.SalesPartnerId) ?? string.Empty,
                r.Code,
                tenantNames.GetValueOrDefault(r.TenantId) ?? string.Empty,
                Date(c.CollectedAtUtc),
                c.BillingPeriod == BillingPeriodKind.Yearly ? "Yıllık" : "Aylık",
                Money(c.GrossAmountMinor),
                Money(c.DiscountAmountMinor),
                Money(c.NetAmountMinor),
                Money(c.CommissionAmountMinor),
                StatusText(c),
                c.PaidAtUtc is { } paid ? Date(paid) : string.Empty,
                c.Status == CommissionStatus.Cancelled ? c.CancelReason ?? string.Empty : c.PaidNote ?? string.Empty,
            ]);
        }

        return Result<SalesPartnerCollectionsExportDto>.Success(
            new SalesPartnerCollectionsExportDto(Encoding.UTF8.GetBytes(sb.ToString()), FileName));
    }

    private static string StatusText(DiscountRedemptionCollection c) => c.Status switch
    {
        CommissionStatus.Accrued => "Tahakkuk",
        CommissionStatus.Paid => "Ödendi",
        _ => c.IsClawback ? "İptal (geri alınacak)" : "İptal",
    };

    private static string Date(DateTime utc) => OperationDayBounds.ToLocal(utc).ToString("dd.MM.yyyy", Turkish);

    private static string Money(long minor) => (minor / 100m).ToString("0.00", Turkish);

    private static void AppendRow(StringBuilder sb, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
                sb.Append(Delimiter);

            var value = ImportCellSafety.ForExport(fields[i]);
            var needsQuote = value.Contains(Delimiter) || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
            sb.Append(needsQuote ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value);
        }

        sb.AppendCsvLine();
    }
}
