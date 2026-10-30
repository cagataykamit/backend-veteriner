namespace Backend.Veteriner.Domain.RecordHistory;

/// <summary>Bir alanın önceki ve yeni değeri; değerler değişmez metin biçimindedir (tarih ISO 8601 UTC, sayı invariant).</summary>
public sealed record RecordFieldChange(string Field, string? OldValue, string? NewValue);
