namespace Backend.Veteriner.Domain.LabResults;

/// <summary>
/// Lab kaydının yaşam döngüsü: istendi, sonuçlandı veya iptal. "Değerlendirme bekliyor" durum değildir
/// (bkz. <see cref="LabResult.AwaitingInterpretation"/>).
/// </summary>
public enum LabResultStatus
{
    Requested = 0,
    Resulted = 1,
    Cancelled = 2
}
