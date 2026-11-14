using System.Security.Cryptography;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.PhoneVerification;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Deneme kaydı (<c>owner-signup</c>) telefon doğrulaması ister (TRIAL-001). Kaydı sınamayan testler SMS akışından geçmeden,
/// her çağrıda yeni numarayla doğrulanmış bir satır ve jeton alır. SMS akışının kendisi
/// <c>SignupPhoneVerificationIntegrationTests</c>'te sınanır.
/// </summary>
internal static class IntegrationTestSignupPhone
{
    public static async Task<(string MobilePhone, string Token)> CreateVerifiedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<ITokenHashService>();
        var utcNow = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var phone = NewPhone();
        var id = Guid.NewGuid();
        var token = Guid.NewGuid().ToString("N");
        var verification = SignupPhoneVerification.Issue(
            id, phone, SignupPhoneVerificationService.ComputeSecretHash(hash, id, SignupPhoneVerificationService.CodePurpose, "000000"),
            null, utcNow);
        verification.TryVerify(
            true, SignupPhoneVerificationService.ComputeSecretHash(hash, id, SignupPhoneVerificationService.TokenPurpose, token), utcNow);

        db.SignupPhoneVerifications.Add(verification);
        await db.SaveChangesAsync();
        return (phone, token);
    }

    /// <summary>Rastgele Türkiye cep numarası (905XXXXXXXXX); testler birbirinin numarasını kullanmasın.</summary>
    public static string NewPhone() => "905" + RandomNumberGenerator.GetInt32(0, 1_000_000_000).ToString("D9");
}
