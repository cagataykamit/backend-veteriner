using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Catalog;

/// <summary>
/// Aşı kataloğu tanımı. <c>TenantId</c> null ise sistem/global; doluysa kiracıya özel.
/// <c>SpeciesId</c> null ise tüm türlere uygun; doluysa yalnızca ilgili türe.
/// </summary>
public sealed class VaccineDefinition : AggregateRoot
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? TenantId { get; private set; }
    public Guid? SpeciesId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Code { get; private set; } = default!;
    public string? Description { get; private set; }
    public int? DefaultNextDueDays { get; private set; }
    public bool IsCore { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private VaccineDefinition() { }

    private VaccineDefinition(
        Guid? tenantId,
        Guid? speciesId,
        string code,
        string name,
        string? description,
        int? defaultNextDueDays,
        bool isCore)
    {
        TenantId = tenantId;
        SpeciesId = speciesId;
        Code = code;
        Name = name;
        Description = description;
        DefaultNextDueDays = defaultNextDueDays;
        IsCore = isCore;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = null;
    }

    public static VaccineDefinition CreateGlobal(
        string code,
        string name,
        string? description = null,
        int? defaultNextDueDays = null,
        bool isCore = true,
        Guid? speciesId = null)
    {
        ValidateInputs(code, name, description, defaultNextDueDays);
        return new VaccineDefinition(
            tenantId: null,
            speciesId,
            NormalizeCode(code),
            name.Trim(),
            TrimOrNull(description),
            defaultNextDueDays,
            isCore);
    }

    public static VaccineDefinition CreateTenant(
        Guid tenantId,
        string code,
        string name,
        Guid? speciesId = null,
        string? description = null,
        int? defaultNextDueDays = null,
        bool isCore = false)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));

        ValidateInputs(code, name, description, defaultNextDueDays);
        return new VaccineDefinition(
            tenantId,
            speciesId,
            NormalizeCode(code),
            name.Trim(),
            TrimOrNull(description),
            defaultNextDueDays,
            isCore);
    }

    public Result UpdateDetails(
        string code,
        string name,
        string? description,
        int? defaultNextDueDays,
        Guid? speciesId,
        bool isCore)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure("VaccineDefinitions.Validation", "Aşı kodu boş olamaz.");
        var normalizedCode = NormalizeCode(code);
        if (normalizedCode.Length > 80)
            return Result.Failure("VaccineDefinitions.Validation", "Aşı kodu en fazla 80 karakter olabilir.");

        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure("VaccineDefinitions.Validation", "Aşı adı boş olamaz.");
        if (name.Trim().Length > 200)
            return Result.Failure("VaccineDefinitions.Validation", "Aşı adı en fazla 200 karakter olabilir.");

        if (description is not null && description.Trim().Length > 1000)
            return Result.Failure("VaccineDefinitions.Validation", "Açıklama en fazla 1000 karakter olabilir.");

        if (defaultNextDueDays is < 1)
            return Result.Failure("VaccineDefinitions.Validation", "Varsayılan sonraki gün sayısı pozitif olmalıdır.");

        Code = normalizedCode;
        Name = name.Trim();
        Description = TrimOrNull(description);
        DefaultNextDueDays = defaultNextDueDays;
        SpeciesId = speciesId;
        IsCore = isCore;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public void Activate()
    {
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// <c>IsCore</c> bayrağını günceller (ör. idempotent seed düzeltmesi). İş kuralı: global sistem
    /// kataloğu için <c>true</c>, kiracı özel tanımlar için genelde <c>false</c>.
    /// </summary>
    public void SetIsCore(bool value)
    {
        IsCore = value;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void ValidateInputs(string code, string name, string? description, int? defaultNextDueDays)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Aşı kodu boş olamaz.", nameof(code));
        if (NormalizeCode(code).Length > 80)
            throw new ArgumentException("Aşı kodu en fazla 80 karakter olabilir.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Aşı adı boş olamaz.", nameof(name));
        if (name.Trim().Length > 200)
            throw new ArgumentException("Aşı adı en fazla 200 karakter olabilir.", nameof(name));
        if (description is not null && description.Trim().Length > 1000)
            throw new ArgumentException("Açıklama en fazla 1000 karakter olabilir.", nameof(description));
        if (defaultNextDueDays is < 1)
            throw new ArgumentException("Varsayılan sonraki gün sayısı pozitif olmalıdır.", nameof(defaultNextDueDays));
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
