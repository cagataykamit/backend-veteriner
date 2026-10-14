namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Şablonun işlem satırı: katalog ürünü veya hizmeti ve miktarı. Fiyat saklanmaz; uygulamada ürün kartından gelir (ADR-018).
/// </summary>
public sealed class ExaminationTemplateLine
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ExaminationTemplateId { get; private set; }
    public int SortOrder { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>Ürünün stok birimi cinsinden miktar (uygulama kaydıyla aynı kural).</summary>
    public decimal Quantity { get; private set; }

    private ExaminationTemplateLine() { }

    internal ExaminationTemplateLine(Guid templateId, int sortOrder, ExaminationTemplateLineDraft draft)
    {
        ExaminationTemplateId = templateId;
        SortOrder = sortOrder;
        ProductId = draft.ProductId;
        Quantity = draft.Quantity;
    }
}

/// <summary>Şablon satırı girdisi (sıra listedeki konumdur).</summary>
public sealed record ExaminationTemplateLineDraft(Guid ProductId, decimal Quantity);
