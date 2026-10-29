using System.Globalization;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Access;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Pets.Timeline;

/// <summary>
/// Uygulama kaydı (ADR-013). Başlık ürün adı, özet "miktar birim"dir; ürün adı için satır eşlemesi asenkron olduğundan
/// ortak kaynak tabanını kullanmaz, aynı sorgu/imleç kuralını (<see cref="PetTimelineSpec{T}"/>) doğrudan uygular.
/// İptal edilenler varsayılan gizlidir. Aşı kaydına bağlı uygulama burada yoktur; olay <c>Vaccination</c> satırıdır (çift satır yok).
/// </summary>
internal sealed class AdministrationTimelineSource(
    IReadRepository<Administration> administrations,
    IReadRepository<Product> products) : IPetTimelineSource
{
    public PetRecordKind Kind => PetRecordKind.Administration;

    public async Task<IReadOnlyList<PetTimelineRow>> ReadAsync(PetTimelineSourceQuery q, CancellationToken ct)
    {
        var spec = new PetTimelineSpec<Administration>(
            q.IncludeVoided
                ? a => a.TenantId == q.TenantId && a.PetId == q.PetId && a.VaccinationId == null
                : a => a.TenantId == q.TenantId && a.PetId == q.PetId && a.VaccinationId == null && a.VoidedAtUtc == null,
            builder => builder.ApplyClinicScope(q.SingleClinicId, q.AccessibleClinicIds),
            a => a.AdministeredAtUtc,
            a => a.Id,
            (int)Kind,
            q);
        var rows = await administrations.ListAsync(spec, ct);
        if (rows.Count == 0)
            return [];

        var productById = (await products.ListAsync(
                new ProductsByIdsSpec(q.TenantId, rows.Select(a => a.ProductId).Distinct().ToArray()), ct))
            .ToDictionary(p => p.Id);

        return rows.Select(a =>
        {
            productById.TryGetValue(a.ProductId, out var product);
            return new PetTimelineRow(
                Kind,
                a.Id,
                a.AdministeredAtUtc,
                a.ClinicId,
                product?.Name ?? string.Empty,
                string.Create(CultureInfo.InvariantCulture, $"{a.Quantity:0.###} {product?.Unit}").TrimEnd(),
                a.IsVoided ? "Voided" : "Active",
                a.IsVoided,
                RelatedExaminationId: a.ExaminationId,
                RelatedVisitId: null,
                RelatedAppointmentId: null,
                UpdatedAtUtc: null,
                AddedAfterFinalization: a.AddedAfterFinalization,
                RelatedTreatmentId: a.TreatmentId);
        }).ToList();
    }
}
