namespace Backend.Veteriner.Application.EDocuments;

/// <summary>
/// Kiracı bazında e-belge ortamı (<c>EDocuments:TestTenantIds</c>). Listedeki kiracı (ör. tanıtım kliniği) canlı API'de de
/// entegratörün test ortamına bağlanır; belgeleri GİB'e gitmez. Test örneği olmayan entegratör bu kiracıya sunulmaz (canlıya
/// düşülmez). Liste dışındaki kiracılar sağlayıcının varsayılan ortamında kalır.
/// </summary>
public sealed class EDocumentEnvironmentOptions
{
    public const string SectionName = "EDocuments";

    public List<Guid> TestTenantIds { get; init; } = [];

    public bool IsTestTenant(Guid tenantId) => TestTenantIds.Contains(tenantId);
}
