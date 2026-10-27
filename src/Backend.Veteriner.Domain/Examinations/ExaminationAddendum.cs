using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Examinations;

/// <summary>
/// Kesinleşmiş muayeneye eklenen gerekçeli ek not. Append-only: oluşturulduktan sonra değişmez, silinmez.
/// Yalnız <see cref="Examination.AddAddendum"/> üretir.
/// </summary>
public sealed class ExaminationAddendum : ITenantOwned
{
    public const int MinReasonLength = 5;
    public const int MaxReasonLength = 500;
    public const int MaxTextLength = 4000;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }
    public Guid ExaminationId { get; private set; }
    public string Reason { get; private set; } = default!;
    public string Text { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    private ExaminationAddendum() { }

    internal ExaminationAddendum(Examination examination, string reason, string text, Guid createdByUserId, DateTime createdAtUtc)
    {
        TenantId = examination.TenantId;
        ClinicId = examination.ClinicId;
        ExaminationId = examination.Id;
        Reason = reason;
        Text = text;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
    }

    internal static Result<(string Reason, string Text)> Validate(string? reason, string? text)
    {
        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length < MinReasonLength || trimmedReason.Length > MaxReasonLength)
        {
            return Result<(string, string)>.Failure(
                "Examinations.Validation",
                $"Gerekçe {MinReasonLength}-{MaxReasonLength} karakter olmalıdır.");
        }

        var trimmedText = text?.Trim() ?? string.Empty;
        if (trimmedText.Length == 0 || trimmedText.Length > MaxTextLength)
        {
            return Result<(string, string)>.Failure(
                "Examinations.Validation",
                $"Ek not metni zorunludur ve en çok {MaxTextLength} karakter olabilir.");
        }

        return Result<(string, string)>.Success((trimmedReason, trimmedText));
    }
}
