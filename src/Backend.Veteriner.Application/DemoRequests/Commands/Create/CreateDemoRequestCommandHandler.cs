using System.Globalization;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Diagnostics;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.DemoRequests;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.DemoRequests.Commands.Create;

/// <summary>
/// Talebi yazar; bildirim adresi yapılandırılmışsa e-postayı outbox'a aynı kayıtla kuyruğa koyar (tek SaveChanges).
/// Talep sahibine e-posta gitmez (anonim uç rastgele adrese e-posta tetiklemesin).
/// </summary>
public sealed class CreateDemoRequestCommandHandler : IRequestHandler<CreateDemoRequestCommand, Result>
{
    private readonly IRepository<DemoRequest> _demoRequests;
    private readonly IEmailSender _email;
    private readonly DemoRequestOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateDemoRequestCommandHandler> _logger;

    public CreateDemoRequestCommandHandler(
        IRepository<DemoRequest> demoRequests,
        IEmailSender email,
        IOptions<DemoRequestOptions> options,
        TimeProvider timeProvider,
        ILogger<CreateDemoRequestCommandHandler> logger)
    {
        _demoRequests = demoRequests;
        _email = email;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result> Handle(CreateDemoRequestCommand request, CancellationToken ct)
    {
        if (request.IsHoneypotFilled)
        {
            _logger.LogInformation("DemoRequest: honeypot filled, ignored.");
            return Result.Success();
        }

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var demoRequest = DemoRequest.Create(
            request.ContactName!,
            request.ClinicName!,
            request.City,
            request.Email!,
            request.Phone,
            request.ClinicSize!.Value,
            request.CurrentSoftware,
            request.Message,
            utcNow);

        await _demoRequests.AddAsync(demoRequest, ct);

        var notifyEmail = _options.NotifyEmail?.Trim();
        if (string.IsNullOrEmpty(notifyEmail))
        {
            _logger.LogInformation(
                "DemoRequest: {DemoRequestId} saved without notification (DemoRequests:NotifyEmail empty), contact {Email}",
                demoRequest.Id, LogMasking.MaskEmail(demoRequest.Email));
        }
        else
        {
            await _email.SendAsync(notifyEmail, $"Vetinity demo talebi — {demoRequest.ClinicName}", BuildBody(demoRequest), ct);
            demoRequest.MarkNotificationQueued(utcNow);
        }

        await _demoRequests.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static string BuildBody(DemoRequest r)
        => new StringBuilder()
            .AppendLine("Tanıtım sitesinden yeni canlı demo talebi (20 dakikalık görüntülü görüşme).")
            .AppendLine()
            .AppendLine($"Ad soyad: {r.ContactName}")
            .AppendLine($"Klinik: {r.ClinicName}")
            .AppendLine($"Şehir: {r.City ?? "-"}")
            .AppendLine($"E-posta: {r.Email}")
            .AppendLine($"Telefon: {r.Phone ?? "-"}")
            .AppendLine($"Klinik büyüklüğü: {ClinicSizeLabel(r.ClinicSize)}")
            .AppendLine($"Bugün kullandığı program/yöntem: {r.CurrentSoftware ?? "-"}")
            .AppendLine()
            .AppendLine("Mesaj:")
            .AppendLine(r.Message ?? "-")
            .AppendLine()
            .AppendLine($"Talep: {r.Id}")
            .AppendLine($"Zaman (UTC): {r.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}")
            .ToString();

    private static string ClinicSizeLabel(DemoClinicSize size) => size switch
    {
        DemoClinicSize.SingleVet => "Tek hekim",
        DemoClinicSize.SmallTeam => "2–5 hekim",
        DemoClinicSize.MultiBranch => "6+ hekim veya birden fazla şube",
        _ => size.ToString()
    };
}
