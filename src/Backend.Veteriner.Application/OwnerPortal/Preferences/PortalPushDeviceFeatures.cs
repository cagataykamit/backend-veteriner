using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.OwnerPortal.Preferences;

public static class PortalPushDeviceErrors
{
    /// <summary>Hesabın push tercihi kapalı; önce tercih açılır, sonra cihaz kaydedilir (veri azaltma).</summary>
    public static readonly Error PushDisabled = new(
        "Portal.Push.Disabled",
        "Anlık bildirim tercihiniz kapalı; önce bildirimleri açın.");
}

/// <summary>
/// Cihaz jetonu kaydı (docs/PORTAL_API_CONTRACT.md §14.2): idempotent; aynı jeton yeniden kaydedilince yenilenir, başka hesaptaysa bu
/// hesaba geçer. <see cref="PreviousToken"/> sağlayıcının yenilediği eski jetondur (bu hesaptaysa silinir).
/// </summary>
public sealed record RegisterPortalPushDeviceCommand(Guid OwnerAccountId, OwnerPushPlatform? Platform, string? Token, string? PreviousToken)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.PushDevices.Register";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId}";
}

/// <summary>Cihaz jetonunu kaldırır (çıkış, izin geri alma). Bilinmeyen ya da başka hesabın jetonu no-op'tur (ayırt edilmez).</summary>
public sealed record RevokePortalPushDeviceCommand(Guid OwnerAccountId, string? Token)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.PushDevices.Revoke";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId}";
}

public sealed class RegisterPortalPushDeviceCommandValidator : AbstractValidator<RegisterPortalPushDeviceCommand>
{
    public RegisterPortalPushDeviceCommandValidator()
    {
        RuleFor(x => x.Platform).NotNull().WithMessage("platform zorunludur.").IsInEnum();
        RuleFor(x => x.Token).Must(t => OwnerPushDevice.NormalizeToken(t) is not null)
            .WithMessage($"token 1–{OwnerPushDevice.MaxTokenLength} karakter olmalıdır.");
        RuleFor(x => x.PreviousToken).MaximumLength(OwnerPushDevice.MaxTokenLength);
    }
}

public sealed class RevokePortalPushDeviceCommandValidator : AbstractValidator<RevokePortalPushDeviceCommand>
{
    public RevokePortalPushDeviceCommandValidator()
        => RuleFor(x => x.Token).Must(t => OwnerPushDevice.NormalizeToken(t) is not null)
            .WithMessage($"token 1–{OwnerPushDevice.MaxTokenLength} karakter olmalıdır.");
}

public sealed class RegisterPortalPushDeviceCommandHandler : IRequestHandler<RegisterPortalPushDeviceCommand, Result>
{
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly IRepository<OwnerPushDevice> _devices;
    private readonly TimeProvider _timeProvider;

    public RegisterPortalPushDeviceCommandHandler(
        IReadRepository<OwnerAccount> accounts,
        IRepository<OwnerPushDevice> devices,
        TimeProvider timeProvider)
    {
        _accounts = accounts;
        _devices = devices;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(RegisterPortalPushDeviceCommand request, CancellationToken ct)
    {
        var account = await _accounts.FirstOrDefaultAsync(new OwnerAccountByIdSpec(request.OwnerAccountId), ct);
        if (account is not { IsActive: true })
            return Result.Failure(OwnerPortalErrors.AccountUnavailable);
        if (!account.PushEnabled)
            return Result.Failure(PortalPushDeviceErrors.PushDisabled);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var token = OwnerPushDevice.NormalizeToken(request.Token)!;
        var platform = request.Platform!.Value;

        var previousToken = OwnerPushDevice.NormalizeToken(request.PreviousToken);
        if (previousToken is not null && previousToken != token)
        {
            var previous = await _devices.FirstOrDefaultAsync(new OwnerPushDeviceByTokenSpec(previousToken), ct);
            if (previous is not null && previous.OwnerAccountId == account.Id)
                await _devices.DeleteAsync(previous, ct);
        }

        var existing = await _devices.FirstOrDefaultAsync(new OwnerPushDeviceByTokenSpec(token), ct);
        if (existing is not null)
            existing.Refresh(account.Id, platform, utcNow);
        else
            await _devices.AddAsync(OwnerPushDevice.Create(account.Id, platform, token, utcNow), ct);

        // Sınır: bu cihaz dahil en fazla MaxDevicesPerAccount; en uzun süredir görülmeyenler düşer.
        var others = (await _devices.ListAsync(new OwnerPushDevicesByAccountSpec(account.Id), ct))
            .Where(d => d.Token != token && d.Token != previousToken)
            .Skip(OwnerPushDevice.MaxDevicesPerAccount - 1)
            .ToList();
        if (others.Count > 0)
            await _devices.DeleteRangeAsync(others, ct);

        try
        {
            await _devices.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı jetonla eşzamanlı iki kayıt: benzersiz indeks ikinciyi reddeder; jeton kayıtlıysa sonuç yine "kayıtlı".
            if (!await _devices.AnyAsync(new OwnerPushDeviceByTokenSpec(token), ct))
                throw;
        }

        return Result.Success();
    }
}

public sealed class RevokePortalPushDeviceCommandHandler : IRequestHandler<RevokePortalPushDeviceCommand, Result>
{
    private readonly IRepository<OwnerPushDevice> _devices;

    public RevokePortalPushDeviceCommandHandler(IRepository<OwnerPushDevice> devices) => _devices = devices;

    public async ValueTask<Result> Handle(RevokePortalPushDeviceCommand request, CancellationToken ct)
    {
        var device = await _devices.FirstOrDefaultAsync(new OwnerPushDeviceByTokenSpec(OwnerPushDevice.NormalizeToken(request.Token)!), ct);
        if (device is null || device.OwnerAccountId != request.OwnerAccountId)
            return Result.Success();

        await _devices.DeleteAsync(device, ct);
        await _devices.SaveChangesAsync(ct);
        return Result.Success();
    }
}
