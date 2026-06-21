# CQRS-14G — Payment list Query DB rollout acceptance / smoke

Payment list CQRS zincirinin (14B + 14C + 14D + 14E + 14F) **operasyonel kapanışı**: rollout sırası, flag açma/rollback, search route guard, read-model health gate ve uçtan uca acceptance test matrisi.

**Production read davranışı değişmedi:** `PaymentProjection:Enabled=false`, `QueryReadModels:PaymentsListReadEnabled=false`, `QueryReadModels:DashboardFinanceReadEnabled=false` (tüm ortam appsettings). Bu faz yalnızca acceptance/smoke güvence seti + rollout dokümanı ekler; production kod davranışı, schema, processor, handler routing, parity/health/backfill logic ve flag default'ları **değiştirilmedi**.

**İlgili dokümanlar:**

- [`cqrs-14b-payment-read-model-schema.md`](cqrs-14b-payment-read-model-schema.md)
- [`cqrs-14c-payment-read-model-projection.md`](cqrs-14c-payment-read-model-projection.md)
- [`cqrs-14d-payment-read-model-reader.md`](cqrs-14d-payment-read-model-reader.md)
- [`cqrs-14e-payment-list-routing.md`](cqrs-14e-payment-list-routing.md)
- [`cqrs-14f-payment-list-backfill-parity-health.md`](cqrs-14f-payment-list-backfill-parity-health.md)
- [`cqrs-13f-payment-finance-rollout-acceptance.md`](cqrs-13f-payment-finance-rollout-acceptance.md) (pattern referansı)

---

## 1. 14B → 14F kısa özeti

| Faz | Ne eklendi | Production etkisi |
|---|---|---|
| **14B** | Query DB `PaymentReadModels` tablo/migration | Read path değişmedi |
| **14C** | Payment create/update event → `PaymentReadModels` idempotent upsert; snapshot client/pet name + normalize + notes | Projection kapalı default; eski payload patlatmaz |
| **14D** | `IPaymentsListReadModelReader` (TenantId+ClinicId zorunlu; `PaidAtUtc DESC, PaymentId DESC`) | Yalnızca reader; handler routing yoktu |
| **14E** | `QueryReadModels:PaymentsListReadEnabled` flag + handler routing | Default **false** — list Command DB okur |
| **14F** | `backfill-payment-read-models`, parity reader, read-model drift health sinyali | Default kapalı; gözlem altyapısı eklendi |

---

## 2. Bu fazda (14G) ne doğrulandı?

`PaymentListRolloutAcceptanceIntegrationTests` rollout zincirini uçtan uca acceptance seviyesinde bağlar:

| # | Senaryo | Doğrulanan |
|---|---|---|
| a | **Default flags false** | Tüm 6 appsettings'te `PaymentProjection:Enabled`, `QueryReadModels:PaymentsListReadEnabled`, `QueryReadModels:DashboardFinanceReadEnabled` = false (rollout default posture) |
| b | **Flag false → Command DB source of truth** | `PaymentReadModels` boş olsa bile list Command DB'den döner; reader/Query path kullanılmaz |
| c | **Backfill + parity + flag true → Query DB source of truth** | Backfill → parity InSync → flag true + boş arama + single clinic → Query DB; dönen alanlar (amount, currency, method, paidAt, client/pet names, pagination, ordering) Command DB ile parity |
| d | **Query DB boş + flag true → fallback yok** | Query path boş `PagedResult` döner (`TotalItems=0`); Command DB'ye fallback yapmaz (14E davranışı korunur) |
| e | **Search dolu + flag true → Command DB guard** | Search dolu iken Query path kullanılmaz; Command DB geniş search davranışı korunur (search parity eksikliği nedeniyle) |
| f | **Health gate** | Projection/list read kapalı + boş read-model → Healthy (sistem bozulmaz); drift + `PaymentsListReadEnabled=true` → Unhealthy; drift + yalnız projection → Degraded; backfill + InSync → Healthy; dead-letter InSync'te bile Unhealthy kalır |
| g | **Rollback** | Flag true Query route; flag false Command route; Query DB drift/boşlukta bile flag false production list davranışını kurtarır |

> Per-bileşen detayları (14E routing unit testleri, 14D reader, 14F backfill/parity/health) ilgili sınıflarda kalır; bu faz onları tekrar etmez, yalnızca rollout zincirini bağlar.

---

## 3. Production default false

`appsettings.json`, `appsettings.Production.json`, `appsettings.Staging.json`, `appsettings.Development.json`, `appsettings.IntegrationTests.json`, `appsettings.LoadTest.json`:

| Ayar | Default |
|---|---|
| `PaymentProjection:Enabled` | **false** |
| `QueryReadModels:PaymentsListReadEnabled` | **false** |
| `QueryReadModels:DashboardFinanceReadEnabled` | **false** |

Doğrulama: `PaymentListRolloutAcceptanceIntegrationTests.RolloutDefaults_*` (rollout posture) + `PaymentsListReadRoutingOptionsTests` (per-flag, 14E).

---

## 4. Rollout sırası (flag açmadan ÖNCE)

```text
migrate-query
  → PaymentProjection:Enabled=true (+ restart) — projection açık, outbox tüketimi başlar
  → backfill-payment-read-models — Command DB ödemelerini PaymentReadModels'e doldur
  → PaymentReadModel parity InSync doğrula (IPaymentReadModelParityReader.GetClinicParityAsync)
  → PaymentProjection health Healthy / read-model InSync doğrula (/health/ready payment-projection)
  → QueryReadModels:PaymentsListReadEnabled=true (+ restart) — list read path Query DB'ye geçer
  → list smoke (flag true + boş arama → Query DB log satırı)
```

| # | Adım | Komut / aksiyon | Doğrulama |
|---|---|---|---|
| 1 | Query migration | `dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query` | `PaymentReadModels` tablo + indexler mevcut |
| 2 | Projection aç | `PaymentProjection__Enabled=true` → restart | Startup log; outbox tüketimi |
| 3 | Backfill | `dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models` | Success; count parity in-sync (mismatch → exit code 2) |
| 4 | Parity | `IPaymentReadModelParityReader.GetClinicParityAsync` | `InSync=true` (count + row sample + recent ordering) |
| 5 | Health | `GET /health/ready` → `payment-projection` | Healthy; `readModelCountInSync=true` |
| 6 | List read aç | `QueryReadModels__PaymentsListReadEnabled=true` → restart | Startup log `PaymentsListReadEnabled=True` |
| 7 | Smoke | `GET /api/v1/payments` (boş arama, tek klinik) | Log: "Payments list generated from Query DB read model" |

> **Altın kural:** `PaymentsListReadEnabled` açmadan önce backfill + parity InSync **zorunludur**. Query DB boşken flag true → boş liste (fallback yok).

### Backfill komutu

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --batch-size 500
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --tenant <guid>
```

---

## 5. Search route guard

Query DB list yolu **yalnızca arama boş/null iken** seçilir. Arama dolu iken bilinçli olarak Command DB path kullanılır:

- **Neden:** 14D reader'ın arama yüzeyi (denormalize `ClientNameNormalized / PetNameNormalized / NotesNormalized / Currency` üzerinden OR) Command DB'nin client email/telefon veya pet breed gibi genişlikleriyle **tam parity değildir**.
- Bu sessiz bir fallback değil, dokümante edilmiş bir "unsupported search route guard"'dır.
- Kullanıcıya eksik sonuç göstermemek için search varken Query path seçilmez; Command DB geniş search davranışı korunur.
- Search parity genişletmesi **ayrı bir faza** bırakılmıştır (kapsam dışı).

Acceptance: `SearchProvided_FlagTrue_Should_UseCommandDbGuard_EvenWhenQueryDbEmpty` (Query DB boş olsa bile search dolu iken Command DB'den sonuç döner).

---

## 6. Query DB fallback yok

Query DB yolu seçildiğinde Command DB'ye fallback **yapılmaz**:

- Reader boş dönerse boş `PagedResult` döner (`TotalItems=0`, `Items=[]`).
- Reader hata fırlatırsa hata yukarı propagate olur; Command DB tekrar denenmez.

Bu davranış, henüz backfill edilmemiş Query DB durumunun sessizce Command DB ile maskelenmesini engeller. Acceptance: `QueryDbEmpty_FlagTrue_EmptySearch_Should_ReturnEmpty_WithoutCommandFallback`.

---

## 7. Health gate (read-model drift)

Read-model durumu mevcut `payment-projection` health entry'sine ek boyut olarak eklenir. Nihai seviye finance kuyruk + read-model drift boyutlarının **en kötüsüdür**.

| projectionEnabled | PaymentsListReadEnabled | drift | Beklenen |
|---|---|---|---|
| false | false | herhangi | **Healthy** (gate kapalı; sinyal hesaplanmaz — boş read-model production-safe) |
| true | true | yok | **Healthy** |
| true | true | var | **Unhealthy** (kullanıcı eksik/yanlış liste görür) |
| true | false | var | **Degraded** (backfill/catch-up penceresi) |
| * | * | * (InSync) | dead-letter / pending-age / retry-waiting severity kuralları korunur (13D) |

Acceptance: `HealthGate_*` testleri rollout zincirini gerçek read-model sayımlarıyla gate'e bağlar.

---

## 8. Rollback

```text
QueryReadModels:PaymentsListReadEnabled=false → restart → handler anında Command DB path'ine döner
```

- Kod geri alımı gerekmez. Startup logunda `PaymentsListReadEnabled=False` ile doğrulanır.
- `PaymentProjection:Enabled` opsiyonel olarak açık bırakılabilir (read-model sıcak tutma); list routing kapalıyken zararsızdır.
- **Query DB'de drift/boşluk olsa bile** flag false production list davranışını kurtarır (Command DB source of truth).
- Read flag kapalıyken read-model drift health sinyali en fazla Degraded üretir (projection açıksa) ve `/health/ready`'yi Unhealthy yapmaz.

Acceptance: `Rollback_FlagFalse_Should_RestoreCommandDb_EvenWhenQueryDbDrifted`.

---

## 9. Smoke / manuel test komutları

```powershell
dotnet build --no-restore

dotnet test --no-restore --filter "FullyQualifiedName~PaymentListRolloutAcceptance"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsListReadRoutingOptions"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentReadModelBackfill"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentReadModelParity"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentProjectionHealth"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentList"
```

Ek manuel doğrulama:

- `backfill-payment-read-models` çalıştır → çıktıda count parity in-sync olduğunu doğrula.
- `IPaymentReadModelParityReader.GetClinicParityAsync` → backfill sonrası `InSync=true`.
- `/health/ready` → `payment-projection` Healthy; flag/projection kapalıyken `readModel*` alanları yok (gate kapalı).
- Flag true + boş arama → log "Payments list generated from Query DB read model"; flag true + dolu arama → Command DB path.
- Finance health/parity/backfill regression filtreleriyle değişmediğini doğrula (`PaymentFinance*`).

---

## 10. Bilinen kapsam dışı

| Konu | Durum |
|---|---|
| Report / export Query DB taşıma | Kapsam dışı (ayrı faz) |
| Dashboard recent payments taşıma | Kapsam dışı |
| Client payment summary taşıma | Kapsam dışı |
| Search parity genişletmesi | Kapsam dışı (search route guard ile Command DB'de kalır) |
| `PaymentReadModel` schema değişikliği | Kapsam dışı |
| `PaymentProjectionProcessor` / `GetPaymentsListQueryHandler` davranış değişikliği | Yapılmadı |
| Feature flag default'larını true yapmak | Yapılmadı |
| Frontend | Yapılmadı |

---

## 11. Bilinen riskler

| Risk | Azaltma |
|---|---|
| Flag true + Query DB boş/eksik | Backfill + parity InSync zorunlu; read flag açıkken count drift → health Unhealthy; acceptance `QueryDbEmpty_*` |
| Rollback unutulursa | Tek env var + restart; startup log flag kontrolü; acceptance `Rollback_*` |
| Search parity eksikliği | Search route guard ile Query path search'te kullanılmaz (dokümante); acceptance `SearchProvided_*` |
| Client/pet rename denormalizasyon drift'i | Beklenen davranış (14F); rename payment event'i ile yansır |
| `/health/ready` ekstra maliyet | Read-model sinyali yalnızca gate açıkken hesaplanır (production default kapalı) |

---

## 12. Commit

**Commit atılmadı, test çalıştırılmadı.** Kullanıcı onayı sonrası ayrı commit.
