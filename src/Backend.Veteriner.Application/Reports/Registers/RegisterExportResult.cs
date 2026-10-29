namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>CSV (UTF-8 BOM), XLSX veya PDF bayt içeriği + indirme dosya adı.</summary>
public sealed record RegisterExportResult(byte[] Content, string FileDownloadName);
