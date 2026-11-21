using System.Linq.Expressions;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Application.Vaccinations;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Ai.PatientSearch;

/// <summary>Bir ölçüte uyan klinik kayıt (yalnız hayvan kimliği, kayıt kimliği, tarih ve kısa metin).</summary>
/// <param name="ScheduledDueAtUtc">Planlı aşının vadesi (yalnız aşıda); gecikme <see cref="VaccinationDueRule.IsOverdueAt"/> ile bellekte hesaplanır.</param>
public sealed record PetSearchMatchRow(Guid PetId, Guid Id, DateTime? OccurredAtUtc, string Text, DateTime? ScheduledDueAtUtc = null);

/// <summary>Arama sonucundaki hayvan satırı.</summary>
public sealed record PetSearchPetRow(
    Guid Id,
    Guid ClientId,
    string Name,
    string SpeciesCode,
    string SpeciesName,
    string? Breed,
    PetGender? Gender,
    bool IsNeutered,
    DateOnly? BirthDate,
    PetStatus Status);

/// <summary>Kayıt tarih aralığı (UTC, bitiş hariç); verilmeyen uç sınırsızdır.</summary>
public sealed record PetSearchPeriod(DateTime? FromUtc, DateTime? ToUtcExclusive);

/// <summary>Muayenenin geliş nedeni, anamnez, bulgular ve değerlendirmesinde terim (en yeni önce).</summary>
public sealed class ExaminationsForPetSearchSpec : Specification<Examination, PetSearchMatchRow>
{
    public ExaminationsForPetSearchSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        IReadOnlyList<string> likePatterns,
        PetSearchPeriod period,
        int take)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (period.FromUtc is { } from)
            Query.Where(e => e.ExaminedAtUtc >= from);
        if (period.ToUtcExclusive is { } to)
            Query.Where(e => e.ExaminedAtUtc < to);
        if (likePatterns.Count > 0)
        {
            Query.Where(PetSearchExpressions.AnyOf(likePatterns.Select(pat => (Expression<Func<Examination, bool>>)(e =>
                EF.Functions.Like(e.VisitReason, pat)
                || (e.Anamnesis != null && EF.Functions.Like(e.Anamnesis, pat))
                || EF.Functions.Like(e.Findings, pat)
                || (e.Assessment != null && EF.Functions.Like(e.Assessment, pat))))));
        }

        Query.OrderByDescending(e => e.ExaminedAtUtc).ThenByDescending(e => e.Id).Take(take);
        Query.Select(e => new PetSearchMatchRow(e.PetId, e.Id, e.ExaminedAtUtc, e.VisitReason));
    }
}

/// <summary>
/// Aşı adında terim ve durum (iptaller hariç). Tarih aralığı uygulanmışta uygulama, planlıda vade tarihine bakar; "herhangi"de
/// ikisinden birine. Gecikme tanımı <see cref="VaccinationDueRule"/>'dan.
/// </summary>
public sealed class VaccinationsForPetSearchSpec : Specification<Vaccination, PetSearchMatchRow>
{
    public VaccinationsForPetSearchSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        IReadOnlyList<string> likePatterns,
        PetSearchVaccineState state,
        PetSearchPeriod period,
        DateTime utcNow,
        int take)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId && v.Status != VaccinationStatus.Cancelled);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (likePatterns.Count > 0)
        {
            Query.Where(PetSearchExpressions.AnyOf(likePatterns.Select(pat =>
                (Expression<Func<Vaccination, bool>>)(v => EF.Functions.Like(v.VaccineName, pat)))));
        }

        switch (state)
        {
            case PetSearchVaccineState.Applied:
                Query.Where(v => v.Status == VaccinationStatus.Applied);
                break;
            case PetSearchVaccineState.Scheduled:
                Query.Where(v => v.Status == VaccinationStatus.Scheduled);
                break;
            case PetSearchVaccineState.Overdue:
                Query.Where(VaccinationDueRule.IsOverdue(VaccinationDueRule.OverdueWindow(utcNow)));
                break;
        }

        var from = period.FromUtc;
        var to = period.ToUtcExclusive;
        if (from is not null || to is not null)
        {
            Query.Where(v =>
                (v.Status == VaccinationStatus.Applied
                    && v.AppliedAtUtc != null
                    && (from == null || v.AppliedAtUtc >= from)
                    && (to == null || v.AppliedAtUtc < to))
                || (v.Status == VaccinationStatus.Scheduled
                    && v.DueAtUtc != null
                    && (from == null || v.DueAtUtc >= from)
                    && (to == null || v.DueAtUtc < to)));
        }

        Query.OrderByDescending(v => v.AppliedAtUtc ?? v.DueAtUtc).ThenByDescending(v => v.Id).Take(take);
        Query.Select(v => new PetSearchMatchRow(
            v.PetId,
            v.Id,
            v.Status == VaccinationStatus.Applied ? v.AppliedAtUtc : v.DueAtUtc,
            v.VaccineName,
            v.Status == VaccinationStatus.Scheduled ? v.DueAtUtc : null));
    }
}

/// <summary>Reçete kalemi adı, başlık ya da içerikte ilaç terimi (iptaller hariç).</summary>
public sealed class PrescriptionsForPetSearchSpec : Specification<Prescription, PetSearchMatchRow>
{
    public PrescriptionsForPetSearchSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        IReadOnlyList<string> likePatterns,
        PetSearchPeriod period,
        int take)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.CancelledAtUtc == null);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (period.FromUtc is { } from)
            Query.Where(p => p.PrescribedAtUtc >= from);
        if (period.ToUtcExclusive is { } to)
            Query.Where(p => p.PrescribedAtUtc < to);
        Query.Where(PetSearchExpressions.AnyOf(likePatterns.Select(pat => (Expression<Func<Prescription, bool>>)(p =>
            EF.Functions.Like(p.Title, pat)
            || EF.Functions.Like(p.Content, pat)
            || p.Items.Any(i => EF.Functions.Like(i.Name, pat))))));

        Query.OrderByDescending(p => p.PrescribedAtUtc).ThenByDescending(p => p.Id).Take(take);
        Query.Select(p => new PetSearchMatchRow(p.PetId, p.Id, p.PrescribedAtUtc, p.Title));
    }
}

/// <summary>Tedavi başlığı ya da açıklamasında ilaç terimi (iptaller hariç).</summary>
public sealed class TreatmentsForPetSearchSpec : Specification<Treatment, PetSearchMatchRow>
{
    public TreatmentsForPetSearchSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        IReadOnlyList<string> likePatterns,
        PetSearchPeriod period,
        int take)
    {
        Query.AsNoTracking();
        Query.Where(t => t.TenantId == tenantId && t.CancelledAtUtc == null);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        if (period.FromUtc is { } from)
            Query.Where(t => t.TreatmentDateUtc >= from);
        if (period.ToUtcExclusive is { } to)
            Query.Where(t => t.TreatmentDateUtc < to);
        Query.Where(PetSearchExpressions.AnyOf(likePatterns.Select(pat => (Expression<Func<Treatment, bool>>)(t =>
            EF.Functions.Like(t.Title, pat) || EF.Functions.Like(t.Description, pat)))));

        Query.OrderByDescending(t => t.TreatmentDateUtc).ThenByDescending(t => t.Id).Take(take);
        Query.Select(t => new PetSearchMatchRow(t.PetId, t.Id, t.TreatmentDateUtc, t.Title));
    }
}

public sealed record PetSearchSpeciesRow(Guid Id, string Code, string Name);

/// <summary>Aktif türler (global katalog), görüntüleme sırasıyla.</summary>
public sealed class ActiveSpeciesForPetSearchSpec : Specification<Species, PetSearchSpeciesRow>
{
    public ActiveSpeciesForPetSearchSpec()
    {
        Query.AsNoTracking();
        Query.Where(s => s.IsActive).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name);
        Query.Select(s => new PetSearchSpeciesRow(s.Id, s.Code, s.Name));
    }
}

/// <summary>Hayvan ölçütleri (kiracı içinde).<paramref name="petIds"/> verilirse yalnız o hayvanlar (klinik kayıt eşleşmesi).</summary>
public sealed class PetsForSearchSpec : Specification<Pet, PetSearchPetRow>
{
    /// <param name="page">Verilirse ada göre sıralı sayfa; verilmezse tümü (sıralama çağıranda).</param>
    public PetsForSearchSpec(Guid tenantId, PetSearchPetCriteria criteria, IReadOnlyCollection<Guid>? petIds, (int Page, int PageSize)? page)
    {
        Query.AsNoTracking();
        PetSearchPetCriteria.Apply(Query, tenantId, criteria, petIds);
        if (page is { } p)
            Query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((p.Page - 1) * p.PageSize).Take(p.PageSize);

        Query.Select(x => new PetSearchPetRow(
            x.Id, x.ClientId, x.Name, x.Species!.Code, x.Species!.Name, x.Breed, x.Gender, x.IsNeutered, x.BirthDate, x.Status));
    }
}

public sealed class PetsForSearchCountSpec : Specification<Pet>
{
    public PetsForSearchCountSpec(Guid tenantId, PetSearchPetCriteria criteria)
        => PetSearchPetCriteria.Apply(Query, tenantId, criteria, null);
}

/// <summary>Hayvan ölçütleri, çözümlenmiş biçimde (tür kimlikleri, doğum tarihi sınırları).</summary>
/// <param name="BornOnOrBefore">Yaş alt sınırından: doğum tarihi bu günden sonra olamaz.</param>
/// <param name="BornAfter">Yaş üst sınırından: doğum tarihi bu günden sonra olmalı.</param>
public sealed record PetSearchPetCriteria(
    IReadOnlyCollection<Guid>? SpeciesIds,
    PetGender? Sex,
    bool? Neutered,
    PetStatus? Status,
    DateOnly? BornOnOrBefore,
    DateOnly? BornAfter)
{
    internal static void Apply(ISpecificationBuilder<Pet> query, Guid tenantId, PetSearchPetCriteria c, IReadOnlyCollection<Guid>? petIds)
    {
        query.Where(p => p.TenantId == tenantId);
        if (petIds is not null)
            query.Where(p => petIds.Contains(p.Id));
        if (c.SpeciesIds is { Count: > 0 } speciesIds)
            query.Where(p => speciesIds.Contains(p.SpeciesId));
        if (c.Sex is { } sex)
            query.Where(p => p.Gender == sex);
        if (c.Neutered is { } neutered)
            query.Where(p => p.IsNeutered == neutered);
        if (c.Status is { } status)
            query.Where(p => p.Status == status);
        if (c.BornOnOrBefore is { } onOrBefore)
            query.Where(p => p.BirthDate != null && p.BirthDate <= onOrBefore);
        if (c.BornAfter is { } after)
            query.Where(p => p.BirthDate != null && p.BirthDate > after);
    }
}

/// <summary>Değişken sayıda terim için VEYA ifadesi (EF'e çevrilir; her terim ayrı parametre).</summary>
public static class PetSearchExpressions
{
    public static IReadOnlyList<string> LikePatterns(IReadOnlyList<string>? terms)
        => terms is null ? [] : terms.Select(ListQueryTextSearch.BuildContainsLikePattern).ToList();

    public static Expression<Func<T, bool>> AnyOf<T>(IEnumerable<Expression<Func<T, bool>>> predicates)
    {
        var list = predicates.ToList();
        if (list.Count == 0)
            return _ => false;

        var parameter = list[0].Parameters[0];
        var body = list[0].Body;
        foreach (var next in list.Skip(1))
            body = Expression.OrElse(body, new ParameterReplacer(next.Parameters[0], parameter).Visit(next.Body));

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    private sealed class ParameterReplacer(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
