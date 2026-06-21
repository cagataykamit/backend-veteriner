# CQRS-14E — Payment list routing + feature flag

**Tür:** Query DB read-model routing + feature flag. **Production list davranışı değişmedi** (flag default false).

**Ön durum:** 14B — `PaymentReadModels` tablo/migration; 14C — projection upsert + snapshot enrichment; 14D — `IPaymentsListReadModelReader` + reader altyapısı (handler routing yoktu).

---

## 1. Bu fazda ne eklendi?

| Bileşen | Açıklama |
|---|---|
| `QueryReadModelsOptions.PaymentsListReadEnabled` | Yeni feature flag, default **false** |
| `appsettings*.json` | 6 dosyada `QueryReadModels:PaymentsListReadEnabled = false` (explicit) |
| `CqrsStartupConfigurationLogger` | Startup log satırına `PaymentsListReadEnabled` eklendi |
| `GetPaymentsListQueryHandler` | Flag kontrollü Query DB reader routing |
| Testler | `PaymentListQueryHandlerFeatureFlagTests`, `PaymentsListReadRoutingOptionsTests` |

**Yapılmadı (kapsam dışı):** `PaymentReadModel` schema/migration, `PaymentProjectionProcessor`, snapshot/event, backfill, parity/health, dashboard recent payments, report/export, search parity için yeni kolon, frontend, permission/endpoint.

---

## 2. `PaymentsListReadEnabled` flag davranışı

`GetPaymentsListQueryHandler` ödeme listesini iki kaynaktan okuyabilir. Kaynak seçimi sadece bu fazda eklenen routing ile belirlenir; mevcut tenant/clinic scope ve doğrulama davranışı **birebir korunur**.

| Koşul | Kaynak |
|---|---|
| `PaymentsListReadEnabled = false` | **Command DB** (mevcut path, birebir) |
| `true` + arama boş/null + klinik tek kliniğe çözüldü | **Query DB** `PaymentReadModels` reader |
| `true` + arama dolu | **Command DB** (search route guard) |
| `true` + klinik kapsamı tek kliniğe çözülemiyor | **Command DB** |

### Query DB yolu seçilmesi için 3 koşul (hepsi birlikte)

1. `PaymentsListReadEnabled = true`.
2. Arama (`request.Search`) `ListQueryTextSearch.Normalize` sonrası `null` (boş/whitespace dahil).
3. Klinik kapsamı `ClinicReadScope.SingleClinicId` ile tek kliniğe çözülmüş.

Bu üç koşul sağlanırsa istek `PaymentsListReadRequest`'e map edilir ve `IPaymentsListReadModelReader.GetListAsync` çağrılır.

### Request mapping (Query DB yolu)

| `GetPaymentsListQuery` | `PaymentsListReadRequest` |
|---|---|
| `tenantId` (context) | `TenantId` |
| `SingleClinicId` (scope) | `ClinicId` |
| `Paging.Page` (clamp `>=1`) | `Page` |
| `Paging.PageSize` (clamp `1..200`) | `PageSize` |
| `ClientId` | `ClientId` |
| `PetId` | `PetId` |
| `Method` | `Method` |
| `PaidFromUtc` | `PaidFromUtc` |
| `PaidToUtc` | `PaidToUtc` |
| — (arama boş) | `SearchContainsLikePattern = null` |

Page/pageSize clamp davranışı Command path ile aynı (`Math.Max(1, …)`, `Math.Clamp(…, 1, 200)`). Date range değerleri Command path ile aynı UTC karşılaştırması ile reader'a geçer.

---

## 3. Flag false → Command DB

Flag kapalı iken handler değişmeden mevcut Command DB path'i çalıştırır: scope resolve, opsiyonel search resolution, `PaymentsFilteredCountSpec` + `PaymentsListFilteredPagedSpec`, ardından client/pet isim hydration. Production default budur.

---

## 4. Flag true + arama boş → Query DB

Query DB yolunda tek `IPaymentsListReadModelReader.GetListAsync` çağrısı yapılır; sonuç `PagedResult<PaymentListItemDto>`'a map edilir. Reader, `PaymentReadModels` üzerinden denormalize client/pet isimleriyle döndüğü için ek Command DB lookup'ı yapılmaz.

---

## 5. Arama varsa neden Command DB kalıyor? (search route guard)

14D reader'ın arama yüzeyi Command DB ile **tam parity değildir**. Command path; client email/telefon veya pet breed gibi lookup/search genişliklerini kullanabilir; reader ise yalnızca denormalize `ClientNameNormalized / PetNameNormalized / NotesNormalized / Currency` üzerinden OR araması yapar.

Kullanıcıya eksik sonuç göstermemek için: **arama dolu iken Query DB seçilmez, bilinçli olarak Command DB path kullanılır.** Bu sessiz bir fallback değil, dokümante edilmiş bir "unsupported search route guard"'dır. Search parity genişletmesi ayrı bir fazda ele alınacaktır.

---

## 6. Query yolu seçilince fallback yok

Query DB yolu seçildiğinde Command DB'ye fallback **yapılmaz**:

- Reader boş dönerse boş `PagedResult` döner (`TotalItems = 0`, `Items = []`).
- Reader hata fırlatırsa hata yukarı propagate olur; Command DB tekrar denenmez.

Bu davranış, Query DB durumunun (ör. henüz backfill edilmemiş) sessizce Command DB ile maskelenmesini engeller.

---

## 7. Production default false

`appsettings.json`, `appsettings.Production.json`, `appsettings.Staging.json`, `appsettings.Development.json`, `appsettings.IntegrationTests.json`, `appsettings.LoadTest.json` dosyalarında `QueryReadModels:PaymentsListReadEnabled = false`. `PaymentsListReadRoutingOptionsTests` bu varsayılanı tüm dosyalar için doğrular.

---

## 8. Rollback

`PaymentsListReadEnabled = false` yapıp uygulamayı yeniden başlatın. Kod geri alımı gerekmez; handler anında Command DB path'ine döner. Startup logundaki `PaymentsListReadEnabled=False` ile doğrulanır.

---

## 9. Kapsam dışı kalan işler

- `PaymentReadModel` schema/migration değişikliği.
- `PaymentProjectionProcessor`, snapshot/event değişikliği.
- Backfill, parity, health.
- Dashboard recent payments taşıma.
- Report/export taşıma.
- Search parity için yeni kolon/lookup.
- Frontend, permission/endpoint değişikliği.
- `GetPaymentById`, client payment-summary değişmedi.

---

## 10. Sonraki faz: 14F notu

14F'de Query DB ödeme listesi için **backfill + parity + health** ele alınacaktır:

- Mevcut Command DB ödemeleri `PaymentReadModels`'e backfill.
- Command ↔ Query liste parity doğrulaması (rollout güvenliği).
- Read-model freshness/health sinyali.

Bu altyapı tamamlanmadan `PaymentsListReadEnabled` production'da açılmamalıdır.

---

## 11. Manuel test listesi

Visual Studio veya terminalde (test bu fazda **çalıştırılmadı**):

```bash
dotnet build --no-restore
dotnet test --no-restore --filter "FullyQualifiedName~GetPaymentsListQueryHandlerTests"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentList"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentReadModelReader"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentProjection"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsListReadRoutingOptions"
```

Ek manuel doğrulama:

- Startup logunda `PaymentsListReadEnabled=False` satırının göründüğünü doğrula.
- Flag true + boş arama ile liste endpoint'inin Query DB'den döndüğünü (log: "Payments list generated from Query DB read model") doğrula.
- Flag true + dolu arama ile Command DB path'inin kullanıldığını doğrula.
