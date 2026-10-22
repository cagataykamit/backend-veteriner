using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Imaging;

/// <summary>Görüntülemenin bağlandığı muayene kiracıda bulunmalı, aynı klinik ve aynı hastaya ait olmalıdır.</summary>
internal static class ImagingExaminationLink
{
    public static Result Validate(Examination? exam, Guid clinicId, Guid petId)
    {
        if (exam is null)
            return Result.Failure("Examinations.NotFound", "Muayene bulunamadı veya kiracıya ait değil.");
        if (exam.ClinicId != clinicId)
        {
            return Result.Failure(
                "ImagingRecords.ExaminationClinicMismatch",
                "Muayenenin kliniği görüntüleme kaydının kliniğiyle uyuşmuyor.");
        }

        return exam.PetId != petId
            ? Result.Failure("ImagingRecords.ExaminationPetMismatch", "Muayenenin hastası görüntüleme kaydının hastasıyla uyuşmuyor.")
            : Result.Success();
    }
}
