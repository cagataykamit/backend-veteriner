namespace Backend.Veteriner.Domain.Files;

/// <summary>
/// Dosyanın yaşam döngüsü: meta yazılır (<see cref="Uploading"/>), içerik yazılınca <see cref="Ready"/>;
/// sahip kayıt iptal edilince veya yarım yükleme temizlenince <see cref="Deleted"/> (erişim kapanır).
/// </summary>
public enum StoredFileStatus
{
    Uploading = 0,
    Ready = 1,
    Deleted = 2
}
