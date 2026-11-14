namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Klinik kaydın konusu (tek yer, ADR-043 K6 / AK-10): hasta ve klinik oluşturulduktan sonra değişmez. Yanlış hasta için kayıt
/// gerekçeyle iptal edilir ve doğru hastaya yeni kayıt açılır; böylece kayıt başka hayvana sessizce taşınmaz.
/// </summary>
public static class RecordSubjectRule
{
    public const string ErrorSuffix = ".SubjectChangeNotAllowed";

    public static Result EnsureUnchanged(Guid currentClinicId, Guid currentPetId, Guid clinicId, Guid petId, string errorPrefix)
        => currentClinicId == clinicId && currentPetId == petId
            ? Result.Success()
            : Result.Failure(
                errorPrefix + ErrorSuffix,
                "Kaydın hastası ve kliniği değiştirilemez; yanlış hasta için kaydı gerekçeyle iptal edip doğru hastaya yeni kayıt açın.");
}
