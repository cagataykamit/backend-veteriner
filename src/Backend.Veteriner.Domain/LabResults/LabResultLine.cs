using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.LabResults;

/// <summary>
/// Lab sonucunun yapılandırılmış değer satırı (REF-001 Dilim 3, ADR-049 K5): parametre, sayısal değer, birim ve sonuçla
/// gelen referans aralığı. Aralık laboratuvara özgüdür, satırla gelir; global referans tablosu yoktur. İşaret elle
/// seçilmez, sınırlardan hesaplanır (<see cref="Flag"/>). Tanı, doz ya da tedavi önermez.
/// </summary>
public sealed class LabResultLine
{
    public const int MaxPerResult = 60;
    public const int ParameterNameMaxLength = 100;
    public const int UnitMaxLength = 30;
    public const int ValueScale = 4;
    public const decimal ValueAbsMax = 1_000_000_000m;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid LabResultId { get; private set; }
    public int SortOrder { get; private set; }
    public string ParameterName { get; private set; } = default!;

    /// <summary>Aynı parametreyi yazım farkından bağımsız eşleyen anahtar (<see cref="TurkishTextKey"/>).</summary>
    public string ParameterKey { get; private set; } = default!;

    public decimal Value { get; private set; }
    public string? Unit { get; private set; }
    public decimal? ReferenceLow { get; private set; }
    public decimal? ReferenceHigh { get; private set; }

    /// <summary>Sınırlardan hesaplanan işaret; iki sınır da yoksa boş (kural <see cref="Evaluate"/>).</summary>
    public LabValueFlag? Flag => Evaluate(Value, ReferenceLow, ReferenceHigh);

    private LabResultLine() { }

    internal LabResultLine(Guid labResultId, int sortOrder, LabResultLineDraft draft)
    {
        LabResultId = labResultId;
        SortOrder = sortOrder;
        ParameterName = draft.ParameterName.Trim();
        ParameterKey = TurkishTextKey.Normalize(draft.ParameterName);
        Value = draft.Value;
        Unit = string.IsNullOrWhiteSpace(draft.Unit) ? null : draft.Unit.Trim();
        ReferenceLow = draft.ReferenceLow;
        ReferenceHigh = draft.ReferenceHigh;
    }

    /// <summary>
    /// Değer alt sınırın altındaysa düşük, üst sınırın üstündeyse yüksek, aralıktaysa (sınır dahil) normal. Tek sınır
    /// verildiyse yalnız o yön değerlendirilir; hiç sınır yoksa işaret yoktur.
    /// </summary>
    public static LabValueFlag? Evaluate(decimal value, decimal? referenceLow, decimal? referenceHigh)
    {
        if (referenceLow is null && referenceHigh is null)
            return null;
        if (referenceLow is { } low && value < low)
            return LabValueFlag.Low;
        if (referenceHigh is { } high && value > high)
            return LabValueFlag.High;
        return LabValueFlag.Normal;
    }

    /// <summary>
    /// Aynı ölçüm mü: parametre ve birim anahtarı aynı olmalı (yazım farkı yok sayılır). Birimi farklı değerler
    /// karşılaştırılmaz.
    /// </summary>
    public bool IsComparableWith(LabResultLine other)
        => ParameterKey == other.ParameterKey
           && TurkishTextKey.Normalize(Unit) == TurkishTextKey.Normalize(other.Unit);

    /// <summary>Önceki değere göre yön; karşılaştırılabilirlik çağıranın denetimidir (<see cref="IsComparableWith"/>).</summary>
    public LabValueTrend TrendFrom(decimal previousValue)
        => Value > previousValue ? LabValueTrend.Up
            : Value < previousValue ? LabValueTrend.Down
            : LabValueTrend.Unchanged;

    internal static Result Validate(LabResultLineDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.ParameterName))
            return Result.Failure("LabResults.Validation", "Parametre adı zorunludur.");
        if (draft.ParameterName.Trim().Length > ParameterNameMaxLength)
            return Result.Failure("LabResults.Validation", $"Parametre adı en fazla {ParameterNameMaxLength} karakter olabilir.");
        if (!IsValidNumber(draft.Value))
            return InvalidNumber("Değer");
        if (draft.Unit is { } unit && unit.Trim().Length > UnitMaxLength)
            return Result.Failure("LabResults.Validation", $"Birim en fazla {UnitMaxLength} karakter olabilir.");
        if (draft.ReferenceLow is { } low && !IsValidNumber(low))
            return InvalidNumber("Referans alt sınırı");
        if (draft.ReferenceHigh is { } high && !IsValidNumber(high))
            return InvalidNumber("Referans üst sınırı");
        if (draft.ReferenceLow > draft.ReferenceHigh)
            return Result.Failure("LabResults.Validation", "Referans alt sınırı üst sınırdan büyük olamaz.");

        return Result.Success();
    }

    private static bool IsValidNumber(decimal value)
        => Math.Abs(value) < ValueAbsMax && decimal.Round(value, ValueScale) == value;

    private static Result InvalidNumber(string label)
        => Result.Failure("LabResults.Validation", $"{label} geçerli bir sayı olmalı; en çok {ValueScale} ondalık basamak girilebilir.");
}

/// <summary>Satır girdisi; doğrulama <see cref="LabResult.ValidateResult(DateTime?, string?, string?, IReadOnlyList{LabResultLineDraft}?)"/> ile yapılır.</summary>
public sealed record LabResultLineDraft(
    string ParameterName,
    decimal Value,
    string? Unit,
    decimal? ReferenceLow,
    decimal? ReferenceHigh);
