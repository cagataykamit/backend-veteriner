using System.Security.Cryptography;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.AppointmentRequests;

/// <summary>
/// Bağlantı akışının e-posta doğrulama kodu (ADR-042 K1): üretim, gönderim ve doğrulama. Düz kod yalnız e-postadadır; özet
/// kod satırının kimliğiyle tuzlanır (portal kodlarıyla aynı yöntem). Süre/deneme/bekleme kuralları
/// <see cref="AppointmentRequestEmailVerification"/>'dadır.
/// </summary>
public sealed class AppointmentRequestVerificationService
{
    private readonly IRepository<AppointmentRequestEmailVerification> _codes;
    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly ITokenHashService _hash;
    private readonly IEmailSender _email;

    public AppointmentRequestVerificationService(
        IRepository<AppointmentRequestEmailVerification> codes,
        IOwnerPortalSecretGenerator secrets,
        ITokenHashService hash,
        IEmailSender email)
    {
        _codes = codes;
        _secrets = secrets;
        _hash = hash;
        _email = email;
    }

    /// <summary>
    /// Yeni kod üretip gönderir; açık kodlar geçersiz olur. Son kod bekleme süresindeyse hiçbir şey yapmaz (yanıt aynı kalır).
    /// Kod ve e-posta (outbox) tek kayıtta yazılır.
    /// </summary>
    public async Task IssueAndSendAsync(
        Guid tenantId,
        Guid clinicId,
        string clinicName,
        string normalizedEmail,
        DateTime utcNow,
        CancellationToken ct)
    {
        var open = await _codes.ListAsync(new OpenAppointmentRequestVerificationsSpec(tenantId, clinicId, normalizedEmail, utcNow), ct);
        if (open.Any(c => c.IsInResendCooldown(utcNow)))
            return;

        foreach (var previous in open)
            previous.Invalidate(utcNow);

        var id = Guid.NewGuid();
        var rawCode = _secrets.CreateNumericCode(OwnerPortalCode.Length);
        var code = AppointmentRequestEmailVerification.Issue(id, tenantId, clinicId, normalizedEmail, ComputeHash(id, rawCode), utcNow);

        var message = AppointmentRequestEmails.VerificationCode(clinicName, rawCode);
        await _email.SendAsync(normalizedEmail, message.Subject, message.Body, ct);
        await _codes.AddAsync(code, ct);
        await _codes.SaveChangesAsync(ct);
    }

    /// <summary>Son kodu doğrular ve tüketir. Yanlış deneme kodda sayılır; değişiklikler izlenir, kaydı çağıran yapar.</summary>
    public async Task<bool> TryConsumeAsync(
        Guid tenantId,
        Guid clinicId,
        string normalizedEmail,
        string? submittedCode,
        DateTime utcNow,
        CancellationToken ct)
    {
        var code = await _codes.FirstOrDefaultAsync(new LatestAppointmentRequestVerificationSpec(tenantId, clinicId, normalizedEmail), ct);
        if (code is null)
            return false;

        var normalized = submittedCode?.Trim() ?? string.Empty;
        var matches = normalized.Length == OwnerPortalCode.Length
                      && CryptographicOperations.FixedTimeEquals(
                          Encoding.ASCII.GetBytes(ComputeHash(code.Id, normalized)),
                          Encoding.ASCII.GetBytes(code.CodeHash));
        return code.TryConsume(matches, utcNow);
    }

    /// <summary>Yanlış denemenin sayacını kalıcı yapar (talep oluşmadığında da deneme hakkı azalmalı).</summary>
    public Task SaveAsync(CancellationToken ct) => _codes.SaveChangesAsync(ct);

    private string ComputeHash(Guid codeId, string rawCode) => _hash.ComputeSha256($"{codeId:N}:{rawCode}");
}
