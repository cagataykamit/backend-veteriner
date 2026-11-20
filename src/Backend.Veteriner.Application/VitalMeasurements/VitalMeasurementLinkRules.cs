using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.VitalMeasurements;

/// <summary>Ölçümün bağlandığı yatış/muayene kuralları (VITAL_SIGNS_API_CONTRACT.md D4, D6); oluşturma ve güncelleme aynı kuralı kullanır.</summary>
internal static class VitalMeasurementLinkRules
{
    public static Result EnsureHospitalizationLink(Hospitalization hospitalization, Guid clinicId, Guid petId)
    {
        if (hospitalization.PetId != petId)
            return Result.Failure("VitalMeasurements.HospitalizationPetMismatch", "Seçilen yatış bu hastaya ait değil.");
        if (hospitalization.ClinicId != clinicId)
            return Result.Failure("VitalMeasurements.HospitalizationClinicMismatch", "Seçilen yatış bu kliniğe ait değil.");
        if (hospitalization.IsCancelled)
            return Result.Failure("Hospitalizations.AlreadyCancelled", "İptal edilmiş yatışa ölçüm eklenemez.");

        return Result.Success();
    }

    public static Result EnsureWithinHospitalization(Hospitalization hospitalization, DateTime measuredAtUtc)
        => hospitalization.CoversInstant(measuredAtUtc)
            ? Result.Success()
            : Result.Failure(
                "VitalMeasurements.OutsideHospitalization",
                "Ölçüm zamanı yatış süresi (kabul–taburcu) içinde olmalıdır.");

    public static Result EnsureExaminationLink(Examination examination, Guid clinicId, Guid petId)
    {
        if (examination.ClinicId != clinicId)
            return Result.Failure("VitalMeasurements.ExaminationClinicMismatch", "Seçilen muayene bu kliniğe ait değil.");
        if (examination.PetId != petId)
            return Result.Failure("VitalMeasurements.ExaminationPetMismatch", "Seçilen muayene bu hastaya ait değil.");

        return Result.Success();
    }
}
