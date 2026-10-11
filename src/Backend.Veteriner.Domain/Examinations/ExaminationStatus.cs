namespace Backend.Veteriner.Domain.Examinations;

/// <summary>Muayene kaydı yaşam döngüsü: Taslak düzenlenir; Kesinleşmiş içerik değişmez, düzeltme yalnız ek notla yapılır.</summary>
public enum ExaminationStatus
{
    Draft = 0,
    Finalized = 1,
}
