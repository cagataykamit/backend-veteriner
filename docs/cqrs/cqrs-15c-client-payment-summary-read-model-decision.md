# CQRS-15C — Client payment summary Query DB karar (route yapılmadı)

**Tür:** Feasibility audit + net karar. **Production kod değişmedi.**

**Sonuç:** `PaymentReadModel` mevcut haliyle `ClientPaymentSummaryDto` alanlarını **tam karşılamıyor**. Bu fazda **Query DB route eklenmedi**; handler Command DB'de kaldı.

**İlgili dokümanlar:** [`cqrs-15a-payment-read-surface-audit.md`](cqrs-15a-payment-read-surface-audit.md) · [`cqrs-15b-dashboard-recent-payments-read-model.md`](cqrs-15b-dashboard-recent-payments-read-model.md) · [`cqrs-14b-payment-read-model-schema.md`](cqrs-14b-payment-read-model-schema.md)

---

## 1. İncelenen yüzey

| Alan | Değer |
|---|---|
| **Endpoint** | `GET /api/v1/clients/{id}/payment-summary` |
| **Controller** | `ClientsController.GetPaymentSummary` |
| **Handler** | `GetClientPaymentSummaryQueryHandler` |
| **Query** | `GetClientPaymentSummaryQuery(Guid Id)` |
| **Spec (Command DB)** | `PaymentsForClientSummaryRowsSpec` |
| **Permission** | `Clients.Read` |

Client detail / ayrı payment history endpoint'i yok; ödeme özeti yalnızca bu endpoint üzerinden sunuluyor.

---

## 2. DTO alan gereksinimleri

### 2.1 `ClientPaymentSummaryDto`

| Alan | Kaynak (Command DB today) | PaymentReadModel |
|---|---|---|
| `ClientId` | Route + client lookup | — (client entity) |
| `ClientName` | `Client.FullName` | — (client entity) |
| `TotalPaymentsCount` | `COUNT(*)` tüm eşleşen ödemeler | Hesaplanabilir (SQL aggregate) |
| `TotalPaidAmount` | Tek currency ise `currencyTotals[0]`; aksi **0** | Hesaplanabilir |
| `CurrencyTotals[]` | `GROUP BY Currency, SUM(Amount)` | Hesaplanabilir |
| `LastPaymentAtUtc` | `MAX(PaidAtUtc)` veya null | Hesaplanabilir |
| `RecentPayments[]` | Son 10, `PaidAtUtc DESC, Id DESC` | Kısmen (aşağıda) |

### 2.2 `ClientPaymentRecentItemDto` (recent, max 10)

| Alan | PaymentReadModel | Durum |
|---|---|---|
| `Id` | `PaymentId` | **yes** |
| `PaidAtUtc` | `PaidAtUtc` | **yes** |
| `ClinicId` | `ClinicId` | **yes** |
| **`ClinicName`** | — | **no — eksik** |
| `PetId` | `PetId` | **yes** |
| `PetName` | `PetName` (denormalize) | **yes** |
| `Amount` | `Amount` | **yes** |
| `Currency` | `Currency` | **yes** |
| `Method` | `Method` | **yes** |
| `Notes` | `Notes` | **yes** |

Invoice/export, examination/appointment adı gibi ek alan **yok** — yalnızca `ClinicName` eksikliği route'u blokluyor.

---

## 3. Ön karar soruları — cevaplar

| # | Soru | Cevap |
|---|---|---|
| 1 | Hangi endpoint/handler? | `GET /clients/{id}/payment-summary` · `GetClientPaymentSummaryQueryHandler` |
| 2 | DTO alanları? | §2 — özet + `recentPayments` (10 kayıt) |
| 3 | PaymentReadModel tam mı? | **Hayır** — `ClinicName` eksik |
| 4 | Eksik alan? | **`ClinicName`** (`ClientPaymentRecentItemDto` zorunlu string). Invoice/export/examination adı yok |
| 5 | Aggregate Command DB'de nasıl? | Tüm eşleşen satırlar bellekte (`ListAsync`); count/currency totals/lastAt in-memory |
| 6 | Query için TenantId+ClientId yeterli mi? | Aggregate + recent için **evet** (index var). Opsiyonel `ClinicId` filtresi de desteklenebilir |
| 7 | Tenant-wide vs aktif clinic? | **Mevcut davranış:** `IClinicContext.ClinicId` doluysa yalnız o klinik; null ise tenant içi tüm klinikler. Contract §19 ile uyumlu |
| 8 | Aynı davranış korunabilir mi? | Query path'te `ClinicName` olmadan **hayır** (boş string parity kırar). Schema veya clinic lookup sonrası **evet** |

---

## 4. Karar: route yapılmadı

**Gerekçe:** Faz kuralı — *“DTO alanları PaymentReadModel ile tam karşılanmıyorsa production code değiştirme.”*

`ClinicName` contract'ta (`docs/BACKEND-CONTRACT-STANDARD.md` §19) açık alan; Command path `ClinicsByTenantIdsSpec` ile dolduruyor. Query path'te boş bırakmak kullanıcıya görünür regresyon olur.

**Eklenmedi:**
- `ClientPaymentSummaryReadEnabled` flag
- `IClientPaymentSummaryReadModelReader`
- Handler routing değişikliği
- Testler

---

## 5. PaymentReadModel yeterlilik özeti

| Bileşen | Yeterli? |
|---|---|
| Recent satır alanları (PaymentId, PaidAt, Amount, …) | **yes** |
| Recent `PetName` | **yes** (denormalize; Command'dan daha iyi) |
| Recent **`ClinicName`** | **no** |
| Summary header `ClientName` | N/A — client entity (her iki yolda aynı) |
| Aggregates (count, currency totals, lastAt) | **yes** — SQL ile hesaplanabilir; schema alanı eksikliği yok |
| Index `TenantId+ClientId+PaidAtUtc` | **partial** — client-wide OK; clinic-scoped yoğun müşteride `(TenantId, ClinicId, ClientId, PaidAtUtc)` tercih edilir (performans, route blokörü değil) |

---

## 6. Mevcut Command DB davranışı (referans)

```text
1. ClientByIdSpec → müşteri yoksa Clients.NotFound
2. clinicId = IClinicContext.ClinicId (opsiyonel)
3. PaymentsForClientSummaryRowsSpec(tenantId, clinicId, clientId) → TÜM satırlar
4. In-memory: count, currencyTotals, lastAt, recent 10
5. Recent için: PetsByTenantIdsSpec + ClinicsByTenantIdsSpec hydration
```

**Risk (mevcut):** Yüksek hacimli müşteride tüm ödemeler bellekte — Query route'ta SQL aggregate tercih edilmeli (schema hazır olduktan sonra).

---

## 7. Önerilen sonraki fazlar

### Seçenek A — Önerilen: `ClinicName` projection enrichment (15C-R)

| Adım | İş |
|---|---|
| 15C-R1 | `PaymentProjectionSnapshot` + `PaymentReadModel` kolon: `ClinicName` (max length clinic adı ile uyumlu) |
| 15C-R2 | Migration Query DB + processor upsert + backfill genişletme + parity alanı |
| 15C-R3 | `IClientPaymentSummaryReadModelReader` — SQL aggregate + recent (Take 10), `ClientPaymentSummaryReadEnabled` flag |
| 15C-R4 | Handler routing (15B pattern: single clinic → Query; tenant-wide/multi-clinic → Command fallback) |

**Not:** Tenant-wide summary için Query path **ClinicId filtresi olmadan** `TenantId+ClientId` yeterli; 15B'den farklı scope modeli.

### Seçenek B — Runtime clinic lookup (15C-L)

Query path'te recent için `Clinic` read-model lookup (report pattern). **Saf PaymentReadModel route değil**; parity mümkün ama ek round-trip.

### Seçenek C — Kapsam daraltma (önerilmez)

DTO'dan `ClinicName` kaldırmak — breaking contract change.

---

## 8. Route eklenseydi — taslaq routing (referans)

*(Uygulanmadı; schema sonrası rehber.)*

| Koşul | Kaynak |
|---|---|
| `ClientPaymentSummaryReadEnabled = false` | Command DB |
| `true` + `TenantId+ClientId` (+ opsiyonel tek clinic) | Query DB |
| Query path seçildi | Fallback **yok**; boş → count 0, recent `[]` |

Scope: Mevcut handler clinic context ile filtreler; tenant-wide kullanıcı tüm klinik ödemelerini görür — Query reader da aynı filtreyi uygulamalı.

---

## 9. Riskler (route sonrası için)

| Risk | Açıklama |
|---|---|
| ClinicName drift | Projection gecikmesi → recent'te boş/eski klinik adı |
| Yüksek hacimli müşteri | Aggregate SQL doğru yazılmazsa performans |
| Clinic-scoped index | `(TenantId, ClinicId, ClientId, PaidAtUtc)` eksikliği |
| Projection lag | Summary count/lastAt gecikmeli |

---

## 10. Kapsam dışı (bu faz)

- Production kod / flag / reader / handler
- Schema / migration / projection
- Payment list, dashboard recent, report/export, GetById
- Test ekleme / çalıştırma
- Commit

---

## 11. Net karar tablosu

| Karar | Sonuç |
|---|---|
| Hemen taşınabilir mi? | **Hayır** |
| Ek schema gerekir mi? | **Evet** — minimum `ClinicName` on `PaymentReadModel` (veya runtime clinic lookup stratejisi) |
| Şimdilik Command DB? | **Evet** |

**Önerilen sıra:** **15C-R** (ClinicName enrichment) → **15C route** (flag + reader + handler) → rollout (projection + backfill + parity + health → flag true).
