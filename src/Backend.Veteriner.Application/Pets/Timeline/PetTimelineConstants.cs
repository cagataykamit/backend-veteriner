using Backend.Veteriner.Application.Pets.Access;

namespace Backend.Veteriner.Application.Pets.Timeline;

public static class PetTimelineConstants
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    /// <summary>Timeline'ın bugün desteklediği türler (ödeme Aşama 3 sonrası eklenecek).</summary>
    public static readonly IReadOnlyList<PetRecordKind> SupportedKinds =
    [
        PetRecordKind.Visit,
        PetRecordKind.Examination,
        PetRecordKind.Treatment,
        PetRecordKind.Prescription,
        PetRecordKind.LabResult,
        PetRecordKind.Vaccination,
        PetRecordKind.Hospitalization,
        PetRecordKind.Administration,
        PetRecordKind.Imaging
    ];
}
