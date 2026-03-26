using System.Security.Claims;
using Backend.Veteriner.Application.Common.Constants;

namespace Backend.Veteriner.Application.Common.Clinic;

/// <summary>
/// JWT <c>clinic_id</c> ile header <c>X-Clinic-Id</c> / sorgu <c>clinicId</c> birleştirmesi.
/// Çakışmada güvenli reddetme için <see cref="Backend.Veteriner.Api.Middleware.ClinicContextMiddleware"/> kullanılır.
/// </summary>
public static class ClinicRequestResolver
{
    public const string HeaderName = "X-Clinic-Id";

    public static ClinicResolveResult Resolve(IEnumerable<Claim> claims, string? headerClinicIdRaw, string? queryClinicIdRaw)
    {
        var claimRaw = claims.FirstOrDefault(c => c.Type == VeterinerClaims.ClinicId)?.Value;
        Guid? claimClinic = Guid.TryParse(claimRaw, out var c) ? c : null;

        Guid? headerClinic = Guid.TryParse(headerClinicIdRaw, out var h) ? h : null;
        Guid? queryClinic = Guid.TryParse(queryClinicIdRaw, out var q) ? q : null;

        // Header ve query aynı anda geliyorsa önce conflict kontrolü yap.
        if (headerClinic.HasValue && queryClinic.HasValue && headerClinic.Value != queryClinic.Value)
            return new ClinicResolveResult(null, ClinicConflict: true);

        var requestClinic = headerClinic ?? queryClinic;

        // Claim ve request birlikte varsa conflict kontrolü.
        if (claimClinic.HasValue && requestClinic.HasValue && claimClinic.Value != requestClinic.Value)
            return new ClinicResolveResult(null, ClinicConflict: true);

        var effective = claimClinic ?? requestClinic;
        return new ClinicResolveResult(effective, ClinicConflict: false);
    }
}

public readonly record struct ClinicResolveResult(Guid? ClinicId, bool ClinicConflict);

