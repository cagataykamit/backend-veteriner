# Performans bulguları (Aşama 9 Dilim 9) — WIP

Durum: YARIM. Yalnız 1k aşaması ölçüldü (aşağıdaki ham çıktı); 10k aşaması (5000 müşteri/10k hasta/365 gün x 100 geliş) bilgisayar yeniden başlatılacağı için kesildi (~3500/38000 geliş yazılmıştı), ölçülmedi. Yorum/öneriler henüz yazılmadı.

## 1k ham ölçüm çıktısı

Ölçek: PerfScale { Clients = 1000, PetsPerClient = 2, Days = 60, VisitsPerDay = 20, Products = 300 }
Üretilen kayıtlar: Clients=1000, Pets=2000, Products=300, StockBatches=1200, Visits=1200, Examinations=1186, Prescriptions=371, Payments=1095, Charges=2355, Vaccinations=299, Appointments=600, StockMovements=2034
Veri üretim süresi: 11,3 sn

| Uç | HTTP | 1. çağrı ms | Ortanca ms | Maks ms | SQL komut | SQL ms (ort.) | Tekrar eden SQL (maks) | Gövde bayt |
|---|---|---|---|---|---|---|---|---|
| Bugün (günlük hacim) | 200 | 350 | 30 | 79 | 8 | 6 | 1 | 22531 |
| Bugün (tam 500 satır) | 200 | 36 | 15 | 22 | 8 | 6 | 1 | 364294 |
| Bugün (501 satır → sınır hatası) | 400 | 21 | 14 | 15 | 7 | 6 | 1 | 380 |
| Müşteri listesi (arama yok, sayfa 1) | 200 | 85 | 3 | 5 | 2 | 1 | 1 | 4421 |
| Müşteri arama (ad parçası) | 200 | 36 | 10 | 13 | 2 | 7 | 1 | 4370 |
| Müşteri arama (telefon) | 200 | 13 | 11 | 15 | 2 | 8 | 1 | 65 |
| Hasta listesi (arama yok) | 200 | 32 | 4 | 7 | 2 | 2 | 1 | 5983 |
| Hasta arama (ad) | 200 | 57 | 24 | 28 | 3 | 19 | 1 | 5956 |
| Hasta arama (mikroçip) | 200 | 25 | 24 | 27 | 3 | 21 | 1 | 358 |
| Hasta timeline (yoğun hasta) | 200 | 147 | 10 | 16 | 10 | 4 | 1 | 25302 |
| Hasta timeline (tipik hasta) | 200 | 6 | 8 | 13 | 10 | 3 | 1 | 532 |
| Hasta özeti (tipik) | 200 | 97 | 7 | 10 | 9 | 3 | 1 | 518 |
| Müşteri bakiyesi | 200 | 44 | 4 | 5 | 5 | 1 | 1 | 299 |
| Stok durumu (liste) | 200 | 44 | 5 | 6 | 5 | 2 | 1 | 19811 |
| Stok durumu (minimum altı) | 200 | 19 | 4 | 4 | 4 | 1 | 1 | 65 |
| Stok hareketleri (son sayfa 1) | 200 | 68 | 9 | 11 | 5 | 5 | 1 | 34904 |
| Stok hareketleri (ürüne göre) | 200 | 24 | 4 | 4 | 5 | 1 | 1 | 3970 |
| Stok hareketleri (tarih aralığı) | 200 | 26 | 6 | 7 | 5 | 3 | 1 | 34903 |
| Stok partileri | 200 | 48 | 5 | 10 | 5 | 3 | 1 | 32742 |
| Borç raporu | 200 | 96 | 5 | 7 | 5 | 4 | 1 | 10657 |
| Borç raporu (arama) | 200 | 82 | 19 | 91 | 5 | 16 | 1 | 1351 |
| Tahsilat raporu | 200 | 87 | 15 | 35 | 8 | 9 | 1 | 19145 |
| Randevu raporu | 200 | 78 | 8 | 8 | 7 | 4 | 1 | 17998 |
| Muayene raporu | 200 | 67 | 10 | 11 | 7 | 6 | 1 | 22586 |
| Aşı raporu | 200 | 46 | 6 | 7 | 7 | 3 | 1 | 24187 |
| Defter: muayene | 200 | 120 | 15 | 18 | 8 | 8 | 1 | 30594 |
| Defter: reçete | 200 | 86 | 7 | 7 | 5 | 3 | 1 | 23466 |
| Defter: aşı | 200 | 38 | 3 | 4 | 3 | 2 | 1 | 27 |
| Defter: ilaç | 200 | 9 | 3 | 3 | 3 | 1 | 1 | 27 |
| Defter: kontrollü madde | 200 | 54 | 6 | 8 | 4 | 3 | 1 | 23218 |
| Dışa aktarım CSV: borç | 200 | 62 | 5 | 7 | 5 | 3 | 1 | 5175 |
| Dışa aktarım XLSX: tahsilat | 200 | 968 | 260 | 389 | 7 | 12 | 1 | 61511 |
| Dışa aktarım CSV: muayene defteri | 200 | 105 | 36 | 44 | 8 | 20 | 1 | 195269 |

## Eksik indeks önerileri (sys.dm_db_missing_index_*)
```
249 | 36 | Charges | [TenantId], [VoidedAtUtc] |  | [ClinicId], [ClientId], [Amount], [AllocatedAmount]
121 | 18 | Charges | [TenantId], [VoidedAtUtc] |  | [ClinicId], [ClientId], [Amount], [CreatedAtUtc], [AllocatedAmount]
101 | 18 | Visits | [TenantId], [PetId], [VoidedAtUtc] |  | 
76 | 18 | Examinations | [TenantId] | [ExaminedAtUtc] | [ClinicId]
70 | 18 | Examinations | [TenantId] | [ExaminedAtUtc] | [ClinicId], [CreatedAtUtc]
58 | 12 | StockMovements | [TenantId] | [MovementType], [OccurredAtUtc] | [ClinicId], [ProductId]
48 | 18 | Payments | [TenantId], [VoidedAtUtc] |  | [AppointmentId], [ExaminationId]
34 | 12 | Payments | [TenantId], [VoidedAtUtc] | [PaidAtUtc] | [ClinicId]
18 | 6 | StockBatches | [TenantId], [ClinicId] | [RemainingQuantity] | 
16 | 6 | Payments | [TenantId], [VoidedAtUtc] | [PaidAtUtc] | [ClinicId], [Amount]
```

## Plan operatörleri: tablo/indeks taraması içeren sorgular (plan önbelleği)
```
5 | 1628 | 2 |  | (@__tenantId_0 uniqueidentifier,@__petIds_1 nvarchar(max) )SELECT [p].[Id], [p].[AlertFlags], [p].[AlertNote], [p].[BirthDate], [p].[Breed], [p].[BreedId], [p].[ClientId], [p].[ColorId], [p].[Gender], [p].[IsNeutered], [p].[MicrochipNumber], [p].[Name], [p].[N
5 | 1292 | 2 |  | (@__tenantId_0 uniqueidentifier,@__clientIds_1 nvarchar(max) )SELECT [c].[Id], [c].[Address], [c].[CreatedAtUtc], [c].[Email], [c].[FullName], [c].[Phone], [c].[PhoneNormalized], [c].[TenantId], [c].[UpdatedAtUtc]  FROM [Clients] AS [c]  WHERE [c].[TenantId] =
6 | 245 | 15 | ClusteredScan  | (@__request_TenantId_0 uniqueidentifier,@__request_FromUtc_1 datetime2(7),@__request_ToUtcExclusive_2 datetime2(7),@__accessible_3 nvarchar(4000),@__p_4 int,@__p_5 int)SELECT [e0].[Id], [e0].[ExaminedAtUtc], [e0].[CreatedAtUtc], [e0].[Status], [c].[FullName] A
6 | 90 | 4 | ClusteredScan  | (@__request_TenantId_0 uniqueidentifier,@__request_FromUtc_1 datetime2(7),@__request_ToUtcExclusive_2 datetime2(7),@__accessible_3 nvarchar(4000))SELECT [p].[ExaminationId], [p].[Title], [p].[Id], [p0].[Name], [p0].[Quantity], [p0].[Instructions], [p0].[Id]  F
6 | 65 | 2 | ClusteredScan  | (@__request_TenantId_0 uniqueidentifier,@__request_FromUtc_1 datetime2(7),@__request_ToUtcExclusive_2 datetime2(7),@__accessible_3 nvarchar(4000))SELECT COUNT(*)  FROM [Examinations] AS [e]  WHERE [e].[TenantId] = @__request_TenantId_0 AND [e].[ExaminedAtUtc] 
5 | 46 | 6 | ClusteredScan  | (@__tenantId_0 uniqueidentifier,@__accessibleClinicIds_1 nvarchar(4000),@__paidFromUtc_2 datetime2(7),@__paidToUtc_3 datetime2(7))SELECT [p].[Id], [p].[AllocatedAmount], [p].[Amount], [p].[AppointmentId], [p].[ClientId], [p].[ClinicId], [p].[Currency], [p].[Ex
5 | 46 | 2 | ClusteredScan  | (@__tenantId_0 uniqueidentifier,@__accessibleClinicIds_1 nvarchar(4000),@__paidFromUtc_Value_2 datetime2(7),@__paidToUtc_Value_3 datetime2(7))SELECT COUNT(*)  FROM [Payments] AS [p]  WHERE [p].[VoidedAtUtc] IS NULL AND [p].[TenantId] = @__tenantId_0 AND [p].[C
11 | 14 | 0 | IndexScan  | (@__userId_0 uniqueidentifier)SELECT [o].[Name]  FROM [UserOperationClaims] AS [u]  INNER JOIN [OperationClaims] AS [o] ON [u].[OperationClaimId] = [o].[Id]  WHERE [u].[UserId] = @__userId_0  ORDER BY [o].[Name]
11 | 14 | 0 |  | (@__userId_0 uniqueidentifier,@__tenantId_1 uniqueidentifier)SELECT [c].[Id], [c].[Address], [c].[City], [c].[Description], [c].[Email], [c].[IsActive], [c].[Name], [c].[Phone], [c].[TenantId]  FROM [UserClinics] AS [u]  INNER JOIN [Clinics] AS [c] ON [u].[Cli
5 | 3 | 0 | ClusteredScan  | (@__tenantId_0 uniqueidentifier,@__clinicIds_1 nvarchar(4000))SELECT [c].[Id], [c].[Address], [c].[City], [c].[Description], [c].[Email], [c].[IsActive], [c].[Name], [c].[Phone], [c].[TenantId]  FROM [Clinics] AS [c]  WHERE [c].[TenantId] = @__tenantId_0 AND [
6 | 2 | 0 | ClusteredScan  | (@__veterinarianIds_0 nvarchar(4000))SELECT [u].[Id], [u].[CreatedAtUtc], [u].[DisplayName], [u].[Email], [u].[EmailConfirmed], [u].[PasswordHash], [u].[UpdatedAtUtc], [u0].[Id], [u0].[Name], [u0].[UserId]  FROM [Users] AS [u]  LEFT JOIN [UserRoles] AS [u0] ON
6 | 0 | 0 | ClusteredScan  | (@__request_TenantId_0 uniqueidentifier,@__request_FromUtc_1 datetime2(7),@__request_ToUtcExclusive_2 datetime2(7),@__accessible_3 nvarchar(4000))SELECT [t].[ExaminationId], [t].[Title]  FROM [Treatments] AS [t]  WHERE [t].[TenantId] = @__request_TenantId_0 AN
6 | 0 | 0 | ClusteredScan  | (@__request_TenantId_0 uniqueidentifier,@__request_FromUtc_1 datetime2(7),@__request_ToUtcExclusive_2 datetime2(7),@__accessible_3 nvarchar(4000))SELECT [a].[ExaminationId], [p].[Name], [a].[Quantity], [p].[Unit], [a].[AddedAfterFinalization], CASE      WHEN [
```

## Tablo satır sayıları
```
Charges | 2355
Visits | 2201
PaymentAllocations | 2186
StockMovements | 2034
Pets | 2000
StockBatches | 1200
Examinations | 1186
Payments | 1095
```

