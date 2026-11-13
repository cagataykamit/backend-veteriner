namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Kürk kataloğundaki satırın türü. Renk (siyah, kızıl, lila…) ile desen (tekir, kaplan çizgili, sivri renk…) FIFe EMS'de
/// olduğu gibi ayrı tutulur; hasta kaydında ikisi ayrı alanlardır.
/// </summary>
public enum PetColorKind
{
    Color = 1,
    Pattern = 2,
}
