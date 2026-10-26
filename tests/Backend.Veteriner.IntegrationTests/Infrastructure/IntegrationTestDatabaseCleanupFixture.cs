[assembly: AssemblyFixture(typeof(Backend.IntegrationTests.Infrastructure.IntegrationTestDatabaseCleanupFixture))]

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Tüm testler bittiğinde bu koşunun LocalDB veritabanlarını siler (<see cref="IntegrationTestDatabaseNames.CleanUp"/>).
/// xUnit v3 assembly fixture'ları assembly-bitti mesajı test host'a iletilmeden önce dispose eder; Dispose / ProcessExit
/// kancaları ise süreç sonuç bildirilir bildirilmez kesilebildiği için temizliği yarım bırakabilir.
/// </summary>
public sealed class IntegrationTestDatabaseCleanupFixture : IDisposable
{
    public void Dispose() => IntegrationTestDatabaseNames.CleanUp();
}
