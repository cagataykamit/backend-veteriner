namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// Employee integration testleri için ek org sabitleri (Task fixture'larından bağımsız boş birim).
/// </summary>
internal static class EmployeeIntegrationTestData
{
    /// <summary>Çalışanı olmayan birim — GetByUnit boş sayfa senaryosu.</summary>
    public static readonly Guid EmptyOrganizationUnitId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-000000000001");
}
