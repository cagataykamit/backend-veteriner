namespace Backend.Veteriner.Domain.Shared;

/// <summary>
/// Son değişiklik zamanı kayıtta tutulan varlık (TIER-009 dış API <c>updatedSince</c> ve sayfa imleci; sıra <c>ChangedAtUtc</c>,
/// <c>Id</c>). Değeri domain yazmaz: kayıt eklenirken ya da değişirken kaydetme anında altyapı tek yerde damgalar; eski satırlar
/// migration anını taşır.
/// </summary>
public interface IChangeTracked
{
    Guid Id { get; }
    DateTime ChangedAtUtc { get; }
}
