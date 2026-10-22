namespace Backend.Veteriner.Domain.Files;

/// <summary>Dosyanın bağlı olduğu kayıt türü; erişim her zaman sahip kaydın izni ve klinik kapsamıyla denetlenir.</summary>
public enum StoredFileOwnerType
{
    ImagingRecord = 1,
    LabResult = 2
}
