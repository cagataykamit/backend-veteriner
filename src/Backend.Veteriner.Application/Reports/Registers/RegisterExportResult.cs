namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>CSV (UTF-8 BOM) veya XLSX bayt içeriği + indirme dosya adı.</summary>
public sealed record RegisterExportResult(byte[] Content, string FileDownloadName);
