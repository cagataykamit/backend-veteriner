using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.EDocuments.Connections;

public sealed class GetEDocumentConnectionQueryHandler : IRequestHandler<GetEDocumentConnectionQuery, Result<EDocumentConnectionDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly EDocumentConnectionService _service;
    private readonly IUnitOfWork _uow;

    public GetEDocumentConnectionQueryHandler(
        EDocumentAccess access, IRepository<EDocumentConnection> connections, EDocumentConnectionService service, IUnitOfWork uow)
    {
        _access = access;
        _connections = connections;
        _service = service;
        _uow = uow;
    }

    public async ValueTask<Result<EDocumentConnectionDto>> Handle(GetEDocumentConnectionQuery request, CancellationToken ct)
    {
        var access = await _access.ResolveClinicSettingsAsync(PermissionCatalog.Clinics.Read, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentConnectionDto>.Failure(access.Error);

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(access.Value!.TenantId, request.ClinicId, tracked: true), ct);
        var dto = await _service.ToDtoAsync(request.ClinicId, connection, ct);
        if (connection is not null)
            await _uow.SaveChangesAsync(ct);
        return Result<EDocumentConnectionDto>.Success(dto);
    }
}

public sealed class StartEDocumentAuthorizationCommandHandler
    : IRequestHandler<StartEDocumentAuthorizationCommand, Result<EDocumentAuthorizationStartDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IEDocumentProviderResolver _providers;
    private readonly EDocumentConnectionService _service;

    public StartEDocumentAuthorizationCommandHandler(
        EDocumentAccess access, IEDocumentProviderResolver providers, EDocumentConnectionService service)
    {
        _access = access;
        _providers = providers;
        _service = service;
    }

    public async ValueTask<Result<EDocumentAuthorizationStartDto>> Handle(StartEDocumentAuthorizationCommand request, CancellationToken ct)
    {
        var access = await _access.ResolveClinicSettingsAsync(PermissionCatalog.Clinics.Update, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentAuthorizationStartDto>.Failure(access.Error);

        var code = request.Provider?.Trim() ?? string.Empty;
        var provider = _providers.Find(code);
        var oauth = _providers.FindOAuth(code);
        if (provider is null || oauth is null)
            return Result<EDocumentAuthorizationStartDto>.Failure("EDocuments.Validation", "Desteklenmeyen e-belge sağlayıcısı.");
        if (!provider.IsConfigured)
            return Result<EDocumentAuthorizationStartDto>.Failure(EDocumentProviderGateway.ProviderNotConfigured());

        var caller = access.Value!;
        var (verifier, challenge) = EDocumentConnectionService.CreatePkcePair();
        var state = _service.CreateState(new EDocumentAuthorizationState(caller.TenantId, request.ClinicId, caller.UserId, provider.ProviderCode, verifier));
        return Result<EDocumentAuthorizationStartDto>.Success(new EDocumentAuthorizationStartDto(oauth.BuildAuthorizationUrl(state, challenge)));
    }
}

/// <summary>
/// OAuth dönüşü: state çözülür (süreli, şifreli), kod belirtece çevrilir, hesap belirlenir, bağlantı sınanır. Hata ayrıntısı
/// günlüğe kod olarak yazılır; belirteç ve kod günlüğe yazılmaz. Tarayıcıya yalnız sonuç kodu döner.
/// </summary>
public sealed class CompleteEDocumentAuthorizationCommandHandler
    : IRequestHandler<CompleteEDocumentAuthorizationCommand, EDocumentAuthorizationOutcome>
{
    private readonly IEDocumentProviderResolver _providers;
    private readonly EDocumentConnectionService _service;
    private readonly EDocumentProviderGateway _gateway;
    private readonly ITenantScope _tenantScope;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly IReadRepository<ClinicTaxpayerSettings> _taxpayerSettings;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompleteEDocumentAuthorizationCommandHandler> _logger;

    public CompleteEDocumentAuthorizationCommandHandler(
        IEDocumentProviderResolver providers,
        EDocumentConnectionService service,
        EDocumentProviderGateway gateway,
        ITenantScope tenantScope,
        IRepository<EDocumentConnection> connections,
        IReadRepository<ClinicTaxpayerSettings> taxpayerSettings,
        IUnitOfWork uow,
        TimeProvider timeProvider,
        ILogger<CompleteEDocumentAuthorizationCommandHandler> logger)
    {
        _providers = providers;
        _service = service;
        _gateway = gateway;
        _tenantScope = tenantScope;
        _connections = connections;
        _taxpayerSettings = taxpayerSettings;
        _uow = uow;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<EDocumentAuthorizationOutcome> Handle(CompleteEDocumentAuthorizationCommand request, CancellationToken ct)
    {
        var state = _service.ReadState(request.State);
        if (state is null || !string.Equals(state.Provider, request.Provider, StringComparison.OrdinalIgnoreCase))
            return Fail(null, "expired", "state çözülemedi veya süresi doldu");

        if (!string.IsNullOrEmpty(request.Error))
            return Fail(state.ClinicId, "denied", $"sağlayıcı hata döndü ({Clip(request.Error)})");
        if (string.IsNullOrWhiteSpace(request.Code))
            return Fail(state.ClinicId, "missing-code", "kod yok");

        var provider = _providers.Find(state.Provider);
        var oauth = _providers.FindOAuth(state.Provider);
        if (provider is null || oauth is null || !provider.IsConfigured)
            return Fail(state.ClinicId, "not-configured", "sağlayıcı yapılandırılmamış");

        var exchanged = await oauth.ExchangeCodeAsync(request.Code, state.Verifier, ct);
        if (!exchanged.IsSuccess)
            return Fail(state.ClinicId, "exchange-failed", $"kod değişimi başarısız ({exchanged.Error!.Kind}, {exchanged.Error.ProviderCode})");
        var credentials = exchanged.Value!;

        var accounts = await oauth.ListAccountsAsync(credentials, ct);
        credentials = accounts.RefreshedCredentials ?? credentials;
        if (!accounts.IsSuccess)
            return Fail(state.ClinicId, "accounts-failed", $"hesaplar okunamadı ({accounts.Error!.Kind})");
        if (accounts.Value!.Count == 0)
            return Fail(state.ClinicId, "no-account", "kullanıcının erişebildiği hesap yok");

        using var tenant = _tenantScope.UseTenant(state.TenantId);
        var account = await ChooseAccountAsync(state, accounts.Value!, ct);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(state.TenantId, state.ClinicId, tracked: true), ct);
        var isNew = connection is null;
        connection ??= EDocumentConnection.Create(state.TenantId, state.ClinicId, provider.ProviderCode, provider.Environment, now);
        connection.Authorize(
            provider.ProviderCode,
            provider.Environment,
            _gateway.ProtectCredentials(credentials),
            account?.AccountId,
            account?.Name,
            state.UserId,
            now);

        if (connection.Status == EDocumentConnectionStatus.Connected)
            await _service.VerifyAsync(connection, ct);

        try
        {
            if (isNew)
                await _connections.AddAsync(connection, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Fail(state.ClinicId, "conflict", "bağlantı eşzamanlı yazıldı");
        }

        _logger.LogInformation(
            "e-belge bağlantısı yetkilendirildi. TenantId={TenantId}, ClinicId={ClinicId}, Provider={Provider}, Status={Status}",
            state.TenantId,
            state.ClinicId,
            provider.ProviderCode,
            connection.Status);

        return new EDocumentAuthorizationOutcome(
            state.ClinicId,
            connection.Status == EDocumentConnectionStatus.AccountSelectionRequired
                ? EDocumentAuthorizationOutcome.SelectAccount
                : EDocumentAuthorizationOutcome.Connected,
            null);
    }

    /// <summary>Tek hesap varsa o; birden çoksa klinik mükellef kimliğiyle eşleşen tek hesap; değilse kullanıcı seçer.</summary>
    private async Task<ProviderAccountInfo?> ChooseAccountAsync(
        EDocumentAuthorizationState state, IReadOnlyList<ProviderAccountInfo> accounts, CancellationToken ct)
    {
        if (accounts.Count == 1)
            return accounts[0];

        var settings = await _taxpayerSettings.FirstOrDefaultAsync(new ClinicTaxpayerSettingsByClinicSpec(state.TenantId, state.ClinicId), ct);
        if (settings?.TaxIdentityNumber is not { } identity)
            return null;
        var matches = accounts.Where(a => a.TaxNumber == identity).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private EDocumentAuthorizationOutcome Fail(Guid? clinicId, string reason, string detail)
    {
        _logger.LogWarning("e-belge bağlantı dönüşü başarısız. ClinicId={ClinicId}, Reason={Reason}, Detail={Detail}", clinicId, reason, detail);
        return new EDocumentAuthorizationOutcome(clinicId, EDocumentAuthorizationOutcome.Error, reason);
    }

    private static string Clip(string value) => value.Length > 60 ? value[..60] : value;
}

/// <summary>
/// API anahtarıyla bağlanma: anahtar sağlayıcıda sınanır, ait olduğu hesap bağlantıya yazılır, anahtar şifreli saklanır.
/// Geçersiz anahtar bağlantıyı değiştirmez (mevcut bağlantı korunur). Anahtar günlüğe ve yanıta yazılmaz.
/// </summary>
public sealed class ConnectEDocumentWithCredentialsCommandHandler
    : IRequestHandler<ConnectEDocumentWithCredentialsCommand, Result<EDocumentConnectionDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IEDocumentProviderResolver _providers;
    private readonly EDocumentConnectionService _service;
    private readonly EDocumentProviderGateway _gateway;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public ConnectEDocumentWithCredentialsCommandHandler(
        EDocumentAccess access,
        IEDocumentProviderResolver providers,
        EDocumentConnectionService service,
        EDocumentProviderGateway gateway,
        IRepository<EDocumentConnection> connections,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _access = access;
        _providers = providers;
        _service = service;
        _gateway = gateway;
        _connections = connections;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<EDocumentConnectionDto>> Handle(ConnectEDocumentWithCredentialsCommand request, CancellationToken ct)
    {
        var access = await _access.ResolveClinicSettingsAsync(PermissionCatalog.Clinics.Update, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentConnectionDto>.Failure(access.Error);

        var code = request.Provider?.Trim() ?? string.Empty;
        var provider = _providers.Find(code);
        var credentialProvider = _providers.FindCredential(code);
        if (provider is null || credentialProvider is null)
            return Result<EDocumentConnectionDto>.Failure("EDocuments.Validation", "Desteklenmeyen e-belge sağlayıcısı.");
        if (!provider.IsConfigured)
            return Result<EDocumentConnectionDto>.Failure(EDocumentProviderGateway.ProviderNotConfigured());

        var apiKey = request.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            return Result<EDocumentConnectionDto>.Failure("EDocuments.Validation", "API anahtarı girilmelidir.");
        if (apiKey.Length > ConnectEDocumentWithCredentialsCommand.MaxApiKeyLength)
            return Result<EDocumentConnectionDto>.Failure("EDocuments.Validation", "API anahtarı çok uzun; anahtarı yeniden kopyalayın.");

        var identified = await credentialProvider.IdentifyAsync(apiKey, ct);
        if (!identified.IsSuccess)
        {
            var error = identified.Error!;
            return Result<EDocumentConnectionDto>.Failure(error.Kind == EDocumentErrorKind.Authorization
                ? new Error("EDocuments.Validation", error.Message)
                : EDocumentErrors.FromProvider(error));
        }

        var caller = access.Value!;
        var account = identified.Value!;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(caller.TenantId, request.ClinicId, tracked: true), ct);
        var isNew = connection is null;
        connection ??= EDocumentConnection.Create(caller.TenantId, request.ClinicId, provider.ProviderCode, provider.Environment, now);
        connection.Authorize(
            provider.ProviderCode,
            provider.Environment,
            _gateway.ProtectCredentials(identified.RefreshedCredentials ?? apiKey),
            account.AccountId,
            account.Name,
            caller.UserId,
            now);

        await _service.VerifyAsync(connection, ct);

        try
        {
            if (isNew)
                await _connections.AddAsync(connection, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<EDocumentConnectionDto>.Failure(
                "EDocuments.ConcurrencyConflict", "Bağlantı aynı anda başka bir işlemle güncellendi; sayfayı yenileyin.");
        }

        return Result<EDocumentConnectionDto>.Success(await _service.ToDtoAsync(request.ClinicId, connection, ct));
    }
}

public sealed class SelectEDocumentAccountCommandHandler : IRequestHandler<SelectEDocumentAccountCommand, Result<EDocumentConnectionDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly EDocumentConnectionService _service;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public SelectEDocumentAccountCommandHandler(
        EDocumentAccess access,
        IRepository<EDocumentConnection> connections,
        EDocumentConnectionService service,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _access = access;
        _connections = connections;
        _service = service;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<EDocumentConnectionDto>> Handle(SelectEDocumentAccountCommand request, CancellationToken ct)
    {
        var access = await _access.ResolveClinicSettingsAsync(PermissionCatalog.Clinics.Update, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentConnectionDto>.Failure(access.Error);

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(access.Value!.TenantId, request.ClinicId, tracked: true), ct);
        if (connection is not { Status: EDocumentConnectionStatus.AccountSelectionRequired })
            return Result<EDocumentConnectionDto>.Failure("EDocuments.InvalidState", "Hesap seçimi beklenmiyor.");

        var accounts = await _service.ListAccountsAsync(connection, ct);
        var chosen = accounts?.FirstOrDefault(a => a.AccountId == request.AccountId?.Trim());
        if (chosen is null)
        {
            await _uow.SaveChangesAsync(ct);
            return Result<EDocumentConnectionDto>.Failure("EDocuments.Validation", "Seçilen hesap bu kullanıcının hesapları arasında yok.");
        }

        var selected = connection.SelectAccount(chosen.AccountId, chosen.Name, _timeProvider.GetUtcNow().UtcDateTime);
        if (!selected.IsSuccess)
            return Result<EDocumentConnectionDto>.Failure(selected.Error);

        await _service.VerifyAsync(connection, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<EDocumentConnectionDto>.Success(await _service.ToDtoAsync(request.ClinicId, connection, ct));
    }
}

public sealed class VerifyEDocumentConnectionCommandHandler : IRequestHandler<VerifyEDocumentConnectionCommand, Result<EDocumentConnectionDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly EDocumentConnectionService _service;
    private readonly IUnitOfWork _uow;

    public VerifyEDocumentConnectionCommandHandler(
        EDocumentAccess access, IRepository<EDocumentConnection> connections, EDocumentConnectionService service, IUnitOfWork uow)
    {
        _access = access;
        _connections = connections;
        _service = service;
        _uow = uow;
    }

    public async ValueTask<Result<EDocumentConnectionDto>> Handle(VerifyEDocumentConnectionCommand request, CancellationToken ct)
    {
        var access = await _access.ResolveClinicSettingsAsync(PermissionCatalog.Clinics.Update, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentConnectionDto>.Failure(access.Error);

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(access.Value!.TenantId, request.ClinicId, tracked: true), ct);
        if (connection is null || connection.Status == EDocumentConnectionStatus.NotConnected)
            return Result<EDocumentConnectionDto>.Failure(EDocumentProviderGateway.NotConnected(connection));

        await _service.VerifyAsync(connection, ct);
        await _uow.SaveChangesAsync(ct);
        return Result<EDocumentConnectionDto>.Success(await _service.ToDtoAsync(request.ClinicId, connection, ct));
    }
}

public sealed class DisconnectEDocumentConnectionCommandHandler : IRequestHandler<DisconnectEDocumentConnectionCommand, Result>
{
    private readonly EDocumentAccess _access;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public DisconnectEDocumentConnectionCommandHandler(
        EDocumentAccess access, IRepository<EDocumentConnection> connections, IUnitOfWork uow, TimeProvider timeProvider)
    {
        _access = access;
        _connections = connections;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(DisconnectEDocumentConnectionCommand request, CancellationToken ct)
    {
        var access = await _access.ResolveClinicSettingsAsync(PermissionCatalog.Clinics.Update, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result.Failure(access.Error);

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(access.Value!.TenantId, request.ClinicId, tracked: true), ct);
        if (connection is null)
            return Result.Success();

        connection.Disconnect(_timeProvider.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
