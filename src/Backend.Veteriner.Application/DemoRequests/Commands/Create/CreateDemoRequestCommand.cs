using Backend.Veteriner.Domain.DemoRequests;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.DemoRequests.Commands.Create;

/// <summary>
/// Tanıtım sitesinden anonim canlı demo talebi (TRIAL-005). Kiracı bağlamı yoktur.
/// </summary>
/// <param name="Website">Tuzak alan (honeypot); doluysa talep sessizce yok sayılır, yanıt yine başarılıdır.</param>
public sealed record CreateDemoRequestCommand(
    string? ContactName,
    string? ClinicName,
    string? City,
    string? Email,
    string? Phone,
    DemoClinicSize? ClinicSize,
    string? CurrentSoftware,
    string? Message,
    bool PrivacyNoticeAccepted,
    string? Website) : IRequest<Result>
{
    public bool IsHoneypotFilled => !string.IsNullOrWhiteSpace(Website);
}
