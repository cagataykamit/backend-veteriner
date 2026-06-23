# CQRS-16B — Payment GetById Query DB route

**Tür:** Payment detail Query DB routing. **Schema/migration/projection değişmedi.**

**Ön karar:** [`cqrs-16a-payment-getbyid-read-model-decision.md`](cqrs-16a-payment-getbyid-read-model-decision.md)

---

## 1. Implemented behavior

`GET /api/v1/payments/{id}` → `GetPaymentByIdQueryHandler`

| Koşul | Kaynak |
|---|---|
| `PaymentsGetByIdReadEnabled = false` | **Command DB** (mevcut, birebir) |
| `PaymentsGetByIdReadEnabled = true` | **Query DB** (`IPaymentGetByIdReadModelReader`) |
| Query path seçildi | Command DB fallback **yok** |
| Query DB satır yok | `Payments.NotFound` |
| Reader exception | Propagate, fallback **yok** |

Query path'te `ClientName` / `PetName` `PaymentReadModel` projection'dan gelir; Command DB client/pet lookup **yapılmaz**.

---

## 2. Flag

| Alan | Değer |
|---|---|
| Option | `QueryReadModelsOptions.PaymentsGetByIdReadEnabled` |
| Default | **false** (6× `appsettings*.json`) |
| Startup log | `PaymentsGetByIdReadEnabled={...}` (`CqrsStartupConfigurationLogger`) |
| Cross-flag | List/report/export bayraklarından **bağımsız** |

---

## 3. No fallback policy

Query path seçildiğinde:

- `IReadRepository<Payment>` / `PaymentByIdSpec` **çağrılmaz**
- Query row yok → **404 + Payments.NotFound** (Command DB'ye düşülmez)
- Reader throw → exception propagate (Command fallback yok)

---

## 4. IDOR / scope behavior

Query path, Command path ile **aynı inline auth** bloklarını korur (satır yüklendikten sonra):

1. Aktif clinic context varsa ve `payment.ClinicId != context.ClinicId` → `Payments.NotFound`
2. `TenantWideClaimNames.IsTenantWide` değilse → `UserClinic.Exists(userId, payment.ClinicId)` → false ise `Payments.NotFound`
3. Cross-tenant → reader `TenantId` filtresi → satır yok → `Payments.NotFound`

Multi-clinic non-tenant-wide kullanıcı: list/report'taki scope fallback **gerekmez**; PK lookup + post-load UserClinic yeterli.

`ClinicReadScopeResolver` kullanılmaz (16A kararı).

---

## 5. Test coverage

| Alan | Sınıf |
|---|---|
| Flag default false (appsettings) | `PaymentsGetByIdReadRoutingOptionsTests` |
| Handler routing + no fallback | `PaymentGetByIdQueryHandlerFeatureFlagTests` |
| Cross-flag isolation | `PaymentGetByIdReadFlagIsolationTests` |
| Mevcut auth regression (Command path) | `GetPaymentByIdQueryHandlerTests` |
| Reader integration (tenant filter, hit/miss) | `PaymentReadModelReaderIntegrationTests` |
| IDOR integration baseline (flag false) | `PaymentDetailIdorIntegrationTests` (değişmedi) |

---

## 6. Rollback

`QueryReadModels:PaymentsGetByIdReadEnabled=false` + restart → handler anında Command DB path'ine döner. Kod geri alımı gerekmez.
