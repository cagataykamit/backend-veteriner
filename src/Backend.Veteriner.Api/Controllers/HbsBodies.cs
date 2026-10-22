using Backend.Veteriner.Domain.Hbs;

namespace Backend.Veteriner.Api.Controllers;

/// <summary>HBS geri kaydı gövdesi (reçete e-Reçete No / aşı ATS belge seri no); bkz. docs/HBS_ASSISTANT_API_CONTRACT.md bölüm 5.</summary>
public sealed record UpdateHbsEntryBody(HbsEntryStatus Status, string? ReferenceNumber, string? NotRequiredReason);
