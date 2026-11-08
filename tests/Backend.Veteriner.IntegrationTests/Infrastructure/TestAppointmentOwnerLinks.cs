using Backend.Veteriner.Application.Appointments.OwnerLinks;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.OwnerPortal.MarketingOptOut;
using Backend.Veteriner.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>Hatırlatma işleyicisini elle kuran testler için gerçek token üretimiyle bağlantı üreticisi (TIER-019).</summary>
public static class TestAppointmentOwnerLinks
{
    public const string PortalBaseUrl = "https://portal.test";

    /// <summary>API kökü: takvim (.ics) bağlantısı buradan üretilir (TIER-020).</summary>
    public const string ApiBaseUrl = "https://api.test";

    public static AppointmentOwnerLinkIssuer Issuer()
        => new(
            new OwnerPortalSecretGenerator(),
            new Sha256TokenHashService(),
            Options.Create(new OwnerPortalOptions { PortalBaseUrl = PortalBaseUrl }),
            Options.Create(new AppOptions { BaseUrl = ApiBaseUrl }));

    /// <summary>Ticari ileti ret bağlantısı (ADR-046); aynı portal kökü.</summary>
    public static MarketingOptOutLinkIssuer MarketingOptOutIssuer()
        => new(
            new OwnerPortalSecretGenerator(),
            new Sha256TokenHashService(),
            Options.Create(new OwnerPortalOptions { PortalBaseUrl = PortalBaseUrl }),
            Options.Create(new AppOptions()));
}
