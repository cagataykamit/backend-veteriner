namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// Çoklu kasa geriye dönük doldurması (TIER-008, migration <c>AddCashRegisters</c>). Tekrar çalıştırılabilir: Ana kasası olmayan her
/// klinik için "Ana kasa" açar; kasası boş nakit ödemeleri, nakit kredi iadelerini ve kasa sayımlarını kliniğin Ana kasasına bağlar;
/// hiç gelir/gider türü olmayan kiracıya varsayılan türleri ekler. Dolu alana dokunmaz. Metin migration tarafından çalıştırılır;
/// değiştirilirse yeni migration gerekir.
/// </summary>
public static class CashRegisterBackfill
{
    public const string Sql = """
        INSERT INTO [CashRegisters] ([Id], [TenantId], [ClinicId], [Code], [Name], [IsMain], [IsActive], [CreatedAtUtc])
        SELECT NEWID(), c.[TenantId], c.[Id], N'ANA', N'Ana kasa', 1, 1, SYSUTCDATETIME()
        FROM [Clinics] c
        WHERE NOT EXISTS (SELECT 1 FROM [CashRegisters] r WHERE r.[TenantId] = c.[TenantId] AND r.[ClinicId] = c.[Id] AND r.[IsMain] = 1)
          AND NOT EXISTS (SELECT 1 FROM [CashRegisters] r WHERE r.[TenantId] = c.[TenantId] AND r.[ClinicId] = c.[Id] AND r.[Code] = N'ANA');

        UPDATE p SET p.[CashRegisterId] = r.[Id]
        FROM [Payments] p
        INNER JOIN [CashRegisters] r ON r.[TenantId] = p.[TenantId] AND r.[ClinicId] = p.[ClinicId] AND r.[IsMain] = 1
        WHERE p.[CashRegisterId] IS NULL AND p.[Method] = 0;

        UPDATE k SET k.[RefundCashRegisterId] = r.[Id]
        FROM [ClientCredits] k
        INNER JOIN [CashRegisters] r ON r.[TenantId] = k.[TenantId] AND r.[ClinicId] = k.[ClinicId] AND r.[IsMain] = 1
        WHERE k.[RefundCashRegisterId] IS NULL AND k.[RefundedAtUtc] IS NOT NULL AND k.[RefundMethod] = 0;

        UPDATE s SET s.[CashRegisterId] = r.[Id]
        FROM [CashCounts] s
        INNER JOIN [CashRegisters] r ON r.[TenantId] = s.[TenantId] AND r.[ClinicId] = s.[ClinicId] AND r.[IsMain] = 1
        WHERE s.[CashRegisterId] IS NULL;

        INSERT INTO [CashMovementCategories] ([Id], [TenantId], [Name], [IsActive], [CreatedAtUtc])
        SELECT NEWID(), t.[Id], d.[Name], 1, SYSUTCDATETIME()
        FROM [Tenants] t
        CROSS JOIN (VALUES (N'Bankaya yatırma'), (N'Masraf'), (N'Bozuk para'), (N'Diğer')) AS d([Name])
        WHERE NOT EXISTS (SELECT 1 FROM [CashMovementCategories] x WHERE x.[TenantId] = t.[Id]);
        """;
}
