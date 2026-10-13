using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Examinations.Access;

/// <summary>
/// Tedavi, reçete ve aşı gibi muayeneye bağlı kayıtların yazma handler'ları için ortak kilit denetimi:
/// ilgili muayenelerden biri kesinleşmişse yazma reddedilir (kural <see cref="Examination.EnsureAcceptsLinkedRecords"/>).
/// Hem hedef hem mevcut (taşınan/ayrılan) muayene verilmelidir; muayenesi bulunamayanlar burada atlanır, varlık denetimi çağıranındır.
/// Kontrollü istisna (ADR-013): uygulama kaydı (administration) bu kilide takılmaz; kesinleşmiş muayeneye gerekçeli "sonradan ekleme" olarak
/// yazılır (kural <c>CreateAdministrationCommandHandler</c> ve <c>Administration.Create</c> içinde), tedavi/reçete/aşı/yatış kilidi değişmez.
/// </summary>
internal static class ExaminationLinkedRecordLock
{
    public static async Task<Result> EnsureOpenAsync(
        IReadRepository<Examination> examinations,
        Guid tenantId,
        IEnumerable<Guid?> examinationIds,
        CancellationToken ct)
    {
        foreach (var id in examinationIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct())
        {
            var exam = await examinations.FirstOrDefaultAsync(new ExaminationByIdSpec(tenantId, id), ct);
            if (exam is null)
                continue;

            var open = exam.EnsureAcceptsLinkedRecords();
            if (!open.IsSuccess)
                return open;
        }

        return Result.Success();
    }

    /// <summary>
    /// Yatış için yalnız muayene bağı kilitlidir (ASAMA7 D7): yatış canlı kayıttır, neden/not/konum/taburcu serbesttir;
    /// yeni yatış kesinleşmiş muayeneye bağlanamaz, mevcut yatışın muayene bağı değişiyorsa mevcut/hedef muayene açık olmalıdır.
    /// Yeni kayıtta <paramref name="existingExaminationId"/> verilmez.
    /// </summary>
    public static async Task<Result> EnsureHospitalizationLinkEditableAsync(
        IReadRepository<Examination> examinations,
        Guid tenantId,
        Guid? existingExaminationId,
        Guid? examinationId,
        CancellationToken ct)
        => existingExaminationId != examinationId
            ? await EnsureOpenAsync(examinations, tenantId, [examinationId, existingExaminationId], ct)
            : Result.Success();

    /// <summary>
    /// Lab sonucu için kısmi kilit: geç gelen sonuç yazılabilsin diye kesinleşmiş muayenede yalnız sonuç alanları (tarih, metin, yorum, not)
    /// serbesttir; test adı, hayvan, klinik veya muayene bağı değişiyorsa mevcut/hedef muayene açık olmalıdır.
    /// Yeni lab kaydı eklemek, istenmiş kayda sonuç girmek ve gerekçeli iptal (ADR-013) kilitli değildir; Create ve Cancel handler'ları bu denetimi çağırmaz.
    /// </summary>
    public static async Task<Result> EnsureLabIdentityEditableAsync(
        IReadRepository<Examination> examinations,
        Guid tenantId,
        LabResult existing,
        Guid clinicId,
        Guid petId,
        Guid? examinationId,
        string testName,
        CancellationToken ct)
        => existing.ChangesIdentity(clinicId, petId, examinationId, testName)
            ? await EnsureOpenAsync(examinations, tenantId, [examinationId, existing.ExaminationId], ct)
            : Result.Success();
}
