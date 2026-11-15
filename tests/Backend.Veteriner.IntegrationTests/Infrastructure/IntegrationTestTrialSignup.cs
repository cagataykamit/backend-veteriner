using System.Security.Cryptography;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.TrialSignup;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Deneme kaydı (<c>owner-signup</c>) doğrulanmış e-posta jetonu ve tek deneme için benzersiz vergi numarası ister (TRIAL-001).
/// Kaydı sınamayan testler e-posta kodu akışından geçmeden doğrulanmış satır ve yeni bir geçerli VKN alır. Akışın kendisi
/// <c>TrialSignupAbuseIntegrationTests</c>'te sınanır.
/// </summary>
internal static class IntegrationTestTrialSignup
{
    public static async Task<(string Token, string TaxNumber)> CreateVerifiedAsync(IServiceProvider services, string email)
    {
        await using var scope = services.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = scope.ServiceProvider.GetRequiredService<ITokenHashService>();
        var utcNow = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        var id = Guid.NewGuid();
        var token = Guid.NewGuid().ToString("N");
        var verification = SignupEmailVerification.Issue(
            id, SignupEmailVerification.NormalizeEmail(email)!,
            SignupEmailVerificationService.ComputeSecretHash(hash, id, SignupEmailVerificationService.CodePurpose, "000000"), null, utcNow);
        verification.TryVerify(
            true, SignupEmailVerificationService.ComputeSecretHash(hash, id, SignupEmailVerificationService.TokenPurpose, token), utcNow);

        db.SignupEmailVerifications.Add(verification);
        await db.SaveChangesAsync();
        return (token, NewTaxNumber());
    }

    /// <summary>Rastgele, sağlama hanesi doğru VKN (10 hane); testler birbirinin numarasını kullanmasın.</summary>
    public static string NewTaxNumber()
    {
        var d = new int[10];
        for (var i = 0; i < 9; i++)
            d[i] = RandomNumberGenerator.GetInt32(0, 10);

        var sum = 0;
        for (var i = 1; i <= 9; i++)
        {
            var t = (d[i - 1] + 10 - i) % 10;
            sum += t == 9 ? 9 : t * (1 << (10 - i)) % 9;
        }

        d[9] = (10 - sum % 10) % 10;
        return string.Concat(d);
    }
}
