using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Muayene şablonu / tedavi paketi (ADR-020 karar 4-5, S4): muayeneye ön dolum olarak uygulanan şikâyet, bulgu ve plan metni
/// ile işlem satırları (ürün veya hizmet + miktar; fiyat kopyalanmaz). <c>TenantId</c> null ise sistem varsayılanı
/// (salt okunur, seed ile gelir, ürün satırı yoktur); doluysa kliniğe özel (eklenir, düzenlenir, pasifleştirilir).
/// Şablon muayeneye bağlanmaz: uygulama metni muayene alanlarına birleştirir, satırları mevcut uygulama/ücret akışıyla yazar.
/// </summary>
public sealed class ExaminationTemplate : AggregateRoot
{
    public const int MaxNameLength = 200;
    public const int MaxVisitReasonLength = 500;
    public const int MaxTextLength = 4000;
    public const int MaxLines = 20;

    public const string ValidationCode = "ExaminationTemplates.Validation";

    private readonly List<ExaminationTemplateLine> _lines = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? TenantId { get; private set; }
    public string Name { get; private set; } = default!;

    /// <summary><see cref="TurkishTextKey.Normalize"/> çıktısı; mükerrer kontrolü ve arama bu kolon üzerinden yapılır.</summary>
    public string NormalizedName { get; private set; } = default!;

    /// <summary>Muayenenin başvuru nedenine (şikâyet) eklenecek metin.</summary>
    public string? VisitReason { get; private set; }

    /// <summary>Muayenenin bulgu alanına eklenecek metin.</summary>
    public string? Findings { get; private set; }

    /// <summary>Muayenenin plan alanına eklenecek metin.</summary>
    public string? Plan { get; private set; }

    /// <summary>
    /// Paket fiyatı (EXAM-007, kullanıcı kararı 2026-10-30): doluysa paket uygulanınca tek "ücret bekliyor" kalemi doğar ve stoklu satırlar
    /// "pakete dahil" uygulanır (kendi kalemlerini doğurmaz, stok düşer); boşsa satırlar kalem kalem ücretlenir. Kural <c>ChargeSuggestion</c>'da.
    /// </summary>
    public decimal? PackagePrice { get; private set; }

    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    /// <summary>SQL Server rowversion — eşzamanlı güncelleme için.</summary>
    public byte[] RowVersion { get; private set; } = default!;

    public IReadOnlyCollection<ExaminationTemplateLine> Lines => _lines;

    public bool IsSystem => TenantId is null;

    private ExaminationTemplate() { }

    /// <summary>Sistem şablonu: yalnız metin (kiracıdan bağımsız ürün bağı olamaz).</summary>
    public static ExaminationTemplate CreateSystem(string name, string? visitReason, string? findings, string? plan, DateTime utcNow)
    {
        var template = new ExaminationTemplate();
        var applied = template.Apply(name, visitReason, findings, plan, [], packagePrice: null);
        if (!applied.IsSuccess)
            throw new ArgumentException(applied.Error.Message, nameof(name));

        template.IsActive = true;
        template.CreatedAtUtc = utcNow;
        return template;
    }

    public static Result<ExaminationTemplate> CreateForTenant(
        Guid tenantId,
        string name,
        string? visitReason,
        string? findings,
        string? plan,
        IReadOnlyList<ExaminationTemplateLineDraft> lines,
        DateTime utcNow,
        decimal? packagePrice = null)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        var template = new ExaminationTemplate { TenantId = tenantId };
        var applied = template.Apply(name, visitReason, findings, plan, lines, packagePrice);
        if (!applied.IsSuccess)
            return Result<ExaminationTemplate>.Failure(applied.Error);

        template.IsActive = true;
        template.CreatedAtUtc = utcNow;
        return Result<ExaminationTemplate>.Success(template);
    }

    /// <summary>Tam güncelleme: ad, metinler ve satırlar birlikte değişir (satırlar baştan yazılır).</summary>
    public Result Update(
        string name,
        string? visitReason,
        string? findings,
        string? plan,
        IReadOnlyList<ExaminationTemplateLineDraft> lines,
        DateTime utcNow,
        decimal? packagePrice = null)
    {
        if (IsSystem)
            return SystemReadOnly();

        var applied = Apply(name, visitReason, findings, plan, lines, packagePrice);
        if (!applied.IsSuccess)
            return applied;

        UpdatedAtUtc = utcNow;
        return Result.Success();
    }

    /// <summary>
    /// Sistem şablonu bakımı (yalnız seed): metni düzeltir ya da şablonu pasifleştirir. Kiracı şablonunda kullanılamaz.
    /// </summary>
    public void ReviseSystemTemplate(string name, string? visitReason, string? findings, string? plan, bool isActive, DateTime utcNow)
    {
        if (!IsSystem)
            throw new InvalidOperationException("Yalnız sistem şablonu düzeltilebilir.");

        var applied = Apply(name, visitReason, findings, plan, [], packagePrice: null);
        if (!applied.IsSuccess)
            throw new ArgumentException(applied.Error.Message, nameof(name));

        IsActive = isActive;
        UpdatedAtUtc = utcNow;
    }

    public Result Activate(DateTime utcNow)
    {
        if (IsSystem)
            return SystemReadOnly();

        if (!IsActive)
        {
            IsActive = true;
            UpdatedAtUtc = utcNow;
        }

        return Result.Success();
    }

    public Result Deactivate(DateTime utcNow)
    {
        if (IsSystem)
            return SystemReadOnly();

        if (IsActive)
        {
            IsActive = false;
            UpdatedAtUtc = utcNow;
        }

        return Result.Success();
    }

    /// <summary>
    /// Satırdaki ürünün şablonda kullanılabilirliği (kaydetme ve uygulama aynı kuralı kullanır): aktif olmalı;
    /// aşı ürünü olamaz (aşı yalnız aşı kaydıyla uygulanır, sonraki doz/hatırlatma o kayıttan doğar).
    /// Hizmet ve kontrollü ürün serbesttir; stok, parti ve ücret kuralları uygulamada mevcut akışla işler.
    /// </summary>
    public static Result CheckLineProduct(Product product)
    {
        if (!product.IsActive)
            return Result.Failure("ExaminationTemplates.ProductInactive", $"Pasif ürün şablonda kullanılamaz: {product.Name}.");

        if (product.Kind == ProductKind.Vaccine)
        {
            return Result.Failure(
                "ExaminationTemplates.VaccineNotAllowed",
                $"Aşı ürünü şablona eklenemez; aşı kaydı ekranından girilir: {product.Name}.");
        }

        return Result.Success();
    }

    /// <summary>Satır miktarı kuralı (kaydetme ve uygulama anındaki düzenleme aynı kuralı kullanır; uygulama kaydı sınırlarıyla aynı).</summary>
    public static Result CheckLineQuantity(decimal quantity)
    {
        if (quantity <= 0
            || quantity > Administration.MaxQuantity
            || decimal.Round(quantity, Administration.QuantityDecimals) != quantity)
        {
            return Invalid(
                $"Miktar 0'dan büyük, en fazla {Administration.MaxQuantity:0} ve en fazla {Administration.QuantityDecimals} ondalıklı olmalıdır.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Uygulanacak satırlar (EXAM-013). <paramref name="selection"/> verilmezse tüm satırlar şablondaki miktarla; verilirse yalnız
    /// listedeki satırlar, listedeki miktarla uygulanır (listede olmayan satır hariçtir, boş liste yalnız metni uygular).
    /// Yalnız şablondaki ürünler seçilebilir; sıra şablondaki sıradır. Şablon değişmez.
    /// </summary>
    public Result<IReadOnlyList<ExaminationTemplateLineDraft>> SelectLinesForApply(IReadOnlyList<ExaminationTemplateLineDraft>? selection)
    {
        var ordered = _lines.OrderBy(l => l.SortOrder).ToList();
        if (selection is null)
        {
            return Result<IReadOnlyList<ExaminationTemplateLineDraft>>.Success(
                ordered.Select(l => new ExaminationTemplateLineDraft(l.ProductId, l.Quantity)).ToList());
        }

        if (selection.Select(s => s.ProductId).Distinct().Count() != selection.Count)
            return Result<IReadOnlyList<ExaminationTemplateLineDraft>>.Failure(ValidationCode, "Aynı satır bir kez seçilebilir.");

        var quantities = new Dictionary<Guid, decimal>();
        foreach (var selected in selection)
        {
            if (ordered.All(l => l.ProductId != selected.ProductId))
            {
                return Result<IReadOnlyList<ExaminationTemplateLineDraft>>.Failure(
                    "ExaminationTemplates.LineNotInTemplate", "Seçilen satır bu şablonda yok; şablonu yenileyip tekrar deneyin.");
            }

            var quantity = CheckLineQuantity(selected.Quantity);
            if (!quantity.IsSuccess)
                return Result<IReadOnlyList<ExaminationTemplateLineDraft>>.Failure(quantity.Error);

            quantities[selected.ProductId] = selected.Quantity;
        }

        return Result<IReadOnlyList<ExaminationTemplateLineDraft>>.Success(ordered
            .Where(l => quantities.ContainsKey(l.ProductId))
            .Select(l => new ExaminationTemplateLineDraft(l.ProductId, quantities[l.ProductId]))
            .ToList());
    }

    private Result Apply(
        string name,
        string? visitReason,
        string? findings,
        string? plan,
        IReadOnlyList<ExaminationTemplateLineDraft> lines,
        decimal? packagePrice)
    {
        var normalizedName = CollapseWhitespace(name);
        if (normalizedName.Length is < 1 or > MaxNameLength)
            return Invalid($"Ad 1-{MaxNameLength} karakter olmalıdır.");

        var reason = NormalizeOptional(visitReason);
        var findingsText = NormalizeOptional(findings);
        var planText = NormalizeOptional(plan);

        if (reason is { Length: > MaxVisitReasonLength })
            return Invalid($"Şikâyet metni en fazla {MaxVisitReasonLength} karakter olabilir.");
        if (findingsText is { Length: > MaxTextLength } || planText is { Length: > MaxTextLength })
            return Invalid($"Bulgu ve plan metni en fazla {MaxTextLength} karakter olabilir.");

        if (lines.Count > MaxLines)
            return Invalid($"Şablonda en fazla {MaxLines} işlem satırı olabilir.");

        foreach (var line in lines)
        {
            if (line.ProductId == Guid.Empty)
                return Invalid("Satır ürünü geçersiz.");

            var quantity = CheckLineQuantity(line.Quantity);
            if (!quantity.IsSuccess)
                return quantity;
        }

        if (lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
            return Invalid("Aynı ürün şablonda bir kez yer alır; miktarı artırın.");

        if (reason is null && findingsText is null && planText is null && lines.Count == 0)
            return Invalid("Şablonda en az bir metin veya işlem satırı olmalıdır.");

        if (packagePrice is { } price
            && (price <= 0 || price > Charge.MaxUnitPrice || decimal.Round(price, Charge.UnitPriceDecimals) != price))
        {
            return Invalid($"Paket fiyatı 0'dan büyük, en fazla {Charge.MaxUnitPrice:0} ve en fazla {Charge.UnitPriceDecimals} ondalıklı olmalıdır.");
        }

        if (packagePrice.HasValue && lines.Count == 0)
            return Invalid("Paket fiyatı yalnız işlem satırı olan şablonda verilebilir.");

        Name = normalizedName;
        NormalizedName = TurkishTextKey.Normalize(normalizedName);
        VisitReason = reason;
        Findings = findingsText;
        Plan = planText;
        PackagePrice = packagePrice;

        _lines.Clear();
        for (var i = 0; i < lines.Count; i++)
            _lines.Add(new ExaminationTemplateLine(Id, i + 1, lines[i]));

        return Result.Success();
    }

    /// <summary>
    /// İstemcinin gördüğü sürüm ile yüklenen sürüm aynı mı (kayıt arada değiştiyse güncelleme reddedilir).
    /// </summary>
    public bool HasRowVersion(byte[] expectedRowVersion)
        => RowVersion is { Length: > 0 } && RowVersion.AsSpan().SequenceEqual(expectedRowVersion);

    private static string CollapseWhitespace(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Result Invalid(string message) => Result.Failure(ValidationCode, message);

    private static Result SystemReadOnly()
        => Result.Failure(
            "ExaminationTemplates.SystemTemplateReadOnly",
            "Sistem şablonları değiştirilemez; kendi şablonunuzu oluşturun.");
}
