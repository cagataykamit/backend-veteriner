# Günlük kasa kontrolü (kasa sayımı) API sözleşmesi — TIER-008

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-008` ("Paket: Kurumsal. Birden fazla kasa, kasalar arası aktarım, kasa bazında gün sonu; ilk dilim günlük kasa kontrol ekranı"), `FIN-002`, ADR-012 (K2 düzeltme yetkisi, ön muhasebe kapsam dışı), ADR-035. İlgili: `DAILY_TRANSACTIONS_REPORT_API_CONTRACT.md` (kasa özeti, beklenen nakdin kaynağı), `FINANCE_CORRECTION_API_CONTRACT.md` (iptal ve gerekçe deseni), `PLAN_FEATURES_API_CONTRACT.md`.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-008-kasa-kontrolu`. **2026-11-09 (dal `feat/tier-008-coklu-kasa`):** sayım kasa bazındadır (`cashRegisterId`), beklenen nakde kasa giriş/çıkışı ve aktarımlar eklenir, paket Profesyonel ve Kurumsal (`CASH_REGISTERS_API_CONTRACT.md`). Migration `AddCashRegisters` eski sayımları Ana kasaya bağlar. Migration `AddCashCounts` (yalnız yerel DB). **Yeni izinler** `CashCounts.Read`, `CashCounts.Create`, `CashCounts.Correct`. §7'deki maddeler **çalışma varsayımıdır, kullanıcı onayı bekliyor.** Ödeme ve tahsilat akışı değişmedi; gün sonu raporları her pakette aynen kalır.

---

## 1) Kavram ve hesap

Kasa ve İstanbul günü için personel kasayı sayar (kasa verilmezse Ana kasa):

| Alan | Anlam | Kaynak |
|---|---|---|
| `openingAmount` | Gün başında kasadaki nakit | Personel girer (0 ile 10.000.000 arası, en çok 2 ondalık) |
| `expectedCashAmount` | Kasanın gün neti: nakit tahsilat − nakit tahsilat iptali − nakit kredi iadesi + kasa girişi − kasa çıkışı + gelen aktarım − giden aktarım | Tahsilat kısmı gün sonu kasa özetinin **nakit satırı** (`CashSummaryLoader`, kasa filtreli); hareketler `CashRegisterTotalsDto`. Kasa hareketi olmayan Ana kasada ve tek kasalı klinikte kasa özetinin nakit satırıyla aynı |
| `expectedClosingAmount` | Kasada olması gereken = `openingAmount + expectedCashAmount` | Domain (`CashCount`) |
| `countedAmount` | Gün sonunda sayılan nakit | Personel girer (açılışla aynı sınır) |
| `differenceAmount` | `countedAmount − expectedClosingAmount`; artı kasa fazlası, eksi kasa açığı | Domain (`CashCount`) |
| `note` | Açıklama, en çok 500 karakter. **Fark sıfır değilse zorunlu** | Personel girer |

- Kart ve Havale-EFT kasaya girmez, hesaba katılmaz. Doğrudan satış nakit tahsilatı (ADR-016) nakit satırının içindedir.
- Beklenen nakit kayıt anında **sabitlenir** (`expectedCashAmount` saklanır). Sonradan gelen ya da iptal edilen tahsilat kaydı değiştirmez; gün ekranı bunu `expectedChangedSinceCount` ile gösterir, personel düzeltme yapar.
- Tutarlar `decimal(18,2)`, yalnız TRY. Hesap toplama ve çıkarmadır; yuvarlama gerekmez (girdi en çok 2 ondalık, kasa özeti 2 ondalıklı tutarların toplamı).
- Gün: İstanbul takvim günü (`OperationDayBounds`), gün sonu raporlarıyla aynı sınır. İleri tarihli gün sayılamaz; geçmiş gün sonradan girilebilir (kaydın zamanı ayrıca tutulur).
- Kasa ve gün başına **en çok bir geçerli** sayım (filtreli benzersiz indeks `UX_CashCounts_TenantId_ClinicId_CashRegisterId_BusinessDate_Active`). Pasif kasaya yeni sayım girilmez (`CashRegisters.Inactive`, 409).

## 2) Kayıt, düzeltme ve iptal (kayıt sessizce ezilmez)

- Kayıt kimin ve ne zaman kaydettiğiyle tutulur (`createdByUserId`, `createdAtUtc`). Tutarlar sonradan değişmez.
- **Düzeltme** = eski kayıt gerekçeyle iptal edilir (`voidedAtUtc`, `voidedByUserId`, `voidReason`), yerine yeni kayıt yazılır (`correctsCashCountId` eskisini gösterir). İkisi tek transaction'dadır: yeni kayıt yazılamazsa iptal de geri alınır. Gün ve klinik düzeltmede değişmez; yanlışsa kayıt iptal edilip doğru güne yeni kayıt girilir. Hiçbir alan değişmiyorsa (açılış, sayılan, beklenen, açıklama) `CashCounts.Validation`.
- **İptal** = kayıt geçersiz olur, gün yeniden sayılabilir. İptal geri alınmaz.
- Gerekçe ücret/ödeme düzeltmesiyle aynı kural: kırpılmış 5–500 karakter (`CorrectionReason`).
- Eşzamanlılık: iki kullanıcı aynı gün için kayıt yazarsa ikincisi `CashCounts.DuplicateForDay` (409). Aynı kaydı aynı anda düzeltme/iptal: `MutationSequence` belirteci, ikincisi `CashCounts.ConcurrencyConflict` (409).
- **Beklenen nakit onayı:** yazma isteği istemcinin ekranda gösterdiği `expectedCashAmount` değerini taşır. Sunucu kasa özetinden yeniden hesaplar; farklıysa hiçbir şey yazılmaz, `CashCounts.ExpectedChanged` (409) döner (mesajda güncel değer). Personel güncel rakamı görmeden fark onaylamaz. Kaydedilen değer her zaman sunucunun hesabıdır.

## 3) Uçlar

Tümü `api/v1/cash-counts`. Kiracı bağlamı zorunlu. **Tek klinik zorunlu:** `clinicId` ya da aktif klinik bağlamı; çözümleme gün sonu raporlarıyla aynı bileşenden (`DailyTransactionsReportFilter` → `RegisterReportFilter`, `IClinicReadScopeResolver`). Klinik yoksa `CashCounts.ClinicScopeRequired` (400); atanmamış klinik `Clinics.AccessDenied` (403); kiracıda olmayan klinik `Clinics.NotFound` (404); JWT `clinic_id` ile sorgu farklıysa `Context.ClinicConflict`.

| Uç | İzin | Paket |
|---|---|---|
| `GET /day?date=yyyy-MM-dd&clinicId=&cashRegisterId=` | `CashCounts.Read` | Her paket |
| `GET ?from=&to=&clinicId=&cashRegisterId=&includeVoided=false&page=1&pageSize=50` (kasa verilmezse tüm kasalar) | `CashCounts.Read` | Her paket |
| `POST /` | `CashCounts.Create` | Pro, Premium |
| `POST /{id}/correct` | `CashCounts.Correct` | Pro, Premium |
| `POST /{id}/void` | `CashCounts.Correct` | Pro, Premium |

Pakette yoksa 403 `Subscriptions.FeatureNotInPlan`, `requiredPlanCode: "Pro"` (`PLAN_FEATURES_API_CONTRACT.md` §2). Denemede seçilen pakete göre davranır. Okuma paket düşünce de açık kalır (veri silinmez, TIER-001 B).

### 3.1 `GET /day` → `CashCountDayDto`

| Alan | Tip | Not |
|---|---|---|
| `clinicId`, `businessDate`, `currency` | | `TRY` |
| `cashRegisterId`, `cashRegisterName` | | Sayılan kasa; klinikte henüz kasa kaydı yoksa `cashRegisterId` boş (Ana kasa) |
| `cash` | `CashSummaryMethodDto` | Kasanın nakit tahsilat satırı **şu anki** hâli (kasa özeti nakit satırı, kasa filtreli): `collectedAmount/Count`, `voidedAmount/Count`, `refundedCreditAmount/Count`, `salesCollectedAmount`, `netAmount` |
| `movements` | `CashRegisterTotalsDto` | `cashNetAmount` (= `cash.netAmount`), `inAmount`, `outAmount`, `transferInAmount`, `transferOutAmount`, `netAmount` |
| `expectedCashAmount` | decimal | Beklenen nakit = `movements.netAmount` |
| `current` | `CashCountDto?` | Günün geçerli sayımı; yoksa `null` |
| `expectedChangedSinceCount` | bool | `current.expectedCashAmount ≠ expectedCashAmount` (sayımdan sonra kasa hareketi değişti) |
| `previous` | `{ businessDate, countedAmount }?` | Bu kasada bu günden önceki son geçerli sayım; açılış tutarı önerisi için. Yoksa `null` |
| `history` | `CashCountDto[]` | Günün tüm kayıtları (iptal ve düzeltilenler dahil), kayıt zamanı azalan |

`CashCountDto`: `id`, `clinicId`, `cashRegisterId`, `businessDate`, `currency`, `openingAmount`, `expectedCashAmount` (kayıt anındaki), `expectedClosingAmount`, `countedAmount`, `differenceAmount`, `note`, `correctsCashCountId`, `createdByUserId`, `createdByName`, `createdAtUtc`, `isVoided`, `voidedAtUtc`, `voidedByUserId`, `voidedByName`, `voidReason`. Adlar `DisplayName`, yoksa e-postadan türetilir (`UserDisplayName`).

### 3.2 `GET /` → `PagedResult<CashCountDto>`

`from`, `to` zorunlu (İstanbul günleri, ikisi dahil, en çok 366 gün; `Registers.DateRangeInvalid` / `Registers.RangeTooLong`). Sıra: gün azalan, kayıt zamanı azalan. `includeVoided=false` yalnız geçerli sayımlar; `true` iptal ve düzeltilenler de. `pageSize` en çok 200. Alanlar: `items`, `page`, `pageSize`, `totalItems`, `totalPages`.

### 3.3 `POST /` → 200 `CashCountDto`

```json
{ "clinicId": "…", "businessDate": "2026-11-08", "openingAmount": 200.00, "countedAmount": 815.00,
  "expectedCashAmount": 620.00, "note": "Para üstü eksik verildi", "cashRegisterId": null }
```

`clinicId` boşsa aktif klinik; `cashRegisterId` boşsa Ana kasa (klinikte yoksa oluşturulur). `expectedCashAmount` gün ekranının `expectedCashAmount` değeridir. Hata: `CashRegisters.NotFound` (404), `CashRegisters.Inactive` (409), `CashCounts.Validation` (400: tutar sınırı, ileri tarih, fark varken açıklama yok, açıklama uzun), `CashCounts.ExpectedChanged` (409), `CashCounts.DuplicateForDay` (409; değiştirmek için düzeltme).

### 3.4 `POST /{id}/correct` → 200 yeni `CashCountDto`

```json
{ "openingAmount": 200.00, "countedAmount": 900.00, "expectedCashAmount": 700.00, "note": null, "reason": "Geç gelen nakit tahsilat" }
```

Hata: `CashCounts.NotFound` (404; aktif klinik bağlamı dışındaki kayıt dahil), `CashCounts.AlreadyVoided` (409), `CashCounts.Validation` (400; gerekçe, değişiklik yok, tutar), `CashCounts.ExpectedChanged`, `CashCounts.ConcurrencyConflict` (409).

### 3.5 `POST /{id}/void` → 204

`{ "reason": "Yanlış klinik seçildi" }`. Hata: `CashCounts.NotFound`, `CashCounts.AlreadyVoided`, `CashCounts.Validation`, `CashCounts.ConcurrencyConflict`.

## 4) Denetim

Yazma komutları `IAuditableRequest`; başarılı ve başarısız her çağrı `AuditLogs`'a yazılır (kullanıcı, IP, korelasyon, istek gövdesi).

| `Action` | `TargetId` |
|---|---|
| `CashCount.Create` | `ClinicId=…;BusinessDate=yyyy-MM-dd` (istekteki klinik; aktif bağlamdan geliyorsa boş) |
| `CashCount.Correct` | `CashCountId=…` (düzeltilen eski kayıt) |
| `CashCount.Void` | `CashCountId=…` |

Kaydın kendisi de kimin, ne zaman kaydettiğini ve iptal ettiğini taşır. Gün sonu dökümüne (`daily-transactions`) sayım olayı yazılmaz: sayım para hareketi değildir.

## 5) Veri modeli (migration `AddCashCounts`)

Tablo `CashCounts`: `Id`, `TenantId`, `ClinicId` (FK `Clinics`, restrict), `CashRegisterId` (FK `CashRegisters`, restrict; `AddCashRegisters` ile) `BusinessDate date`, `OpeningAmount`, `ExpectedCashAmount`, `CountedAmount` (`decimal(18,2)`), `Note nvarchar(500)`, `Currency nvarchar(3)`, `CorrectsCashCountId`, `CreatedByUserId`, `CreatedAtUtc`, `VoidedAtUtc`, `VoidedByUserId`, `VoidReason nvarchar(500)`, `MutationSequence bigint` (concurrency). `CHECK`: açılış ve sayılan ≥ 0; üç iptal alanı birlikte boş ya da birlikte dolu. İndeksler: `(TenantId, ClinicId, CashRegisterId, BusinessDate)` benzersiz, `VoidedAtUtc IS NULL` filtreli; `(TenantId, ClinicId, BusinessDate, CreatedAtUtc)`. Kiracı filtresi `ITenantOwned` ile (ADR-040). Mevcut tablolara dokunulmaz; veri taşıma yok. Paylaşılan/üretim DB'ye uygulanması kullanıcı onayı ister.

## 6) Yetkiler

| İzin | Varsayılan roller |
|---|---|
| `CashCounts.Read` | Admin, Owner, ClinicAdmin, Sekreter |
| `CashCounts.Create` | Admin, Owner, ClinicAdmin, Sekreter |
| `CashCounts.Correct` (düzeltme ve iptal) | Admin, Owner, ClinicAdmin |

Veteriner almaz. İzinler `RolePermissionBindings` ile seed'de idempotent eklenir; **mevcut özel roller elle almalı** (deploy notu). Sayım ekranı beklenen nakdi gösterdiği için okuma izni ödeme okuma izni olan rollere verildi; uç ayrıca `Payments.Read` istemez.

## 7) Çalışma varsayımları (kullanıcı onayı bekliyor)

1. ~~Paket yalnız Kurumsal~~ 2026-11-09 kullanıcı kararı: Profesyonel ve Kurumsal; okuma her pakette açık.
2. Fark sıfır değilse açıklama zorunlu. Gevşetmek veri taşıma gerektirmez.
3. Beklenen nakit = günün nakit tahsilat neti (kredi iadesi düşülmüş). Açılış tutarını sistem türetmez, personel girer; önceki günün sayılan nakdi yalnız öneri olarak döner. 2026-11-09: gün içi kasaya giriş/çıkış (bankaya yatırma, masraf, bozuk para) ve aktarımlar kendi satırında beklenen nakde girer; artık fark olarak görünmez.
4. Sayım düzeltmesi ve iptali yönetici düzeyinde (ADR-012 K2 ile aynı); resepsiyon kaydeder ama düzeltemez.
5. Beklenen nakit kayıtta sabitlenir; sonraki tahsilat/iptal kaydı otomatik güncellemez, ekran uyarır.
6. Geçmiş gün sayımı serbest; sınır yok.

## 8) Sonraki dilimler (bu dilimde yok)

- ~~Birden fazla kasa, ödemede kasa seçimi ve kasa bazında gün sonu, aktarım, giriş/çıkış~~: yapıldı (`CASH_REGISTERS_API_CONTRACT.md`).
- Banka hesapları; kart ve havalenin hesaba bağlanması.
- Günü kilitleme (sayımdan sonra o güne geriye dönük tahsilat engeli) ve sayım için yazdırma/PDF.
- Şubeler özetinde (TIER-007) kasa farkı sütunu.

## 9) Testler

- Domain: `CashCountTests` (fark hesabı, tutar sınırları, fark varken açıklama, ileri tarih, düzeltmenin eski kaydı ezmemesi ve bağlı yeni kayıt, iptal).
- Application: `CashCountHandlerTests` (beklenen nakit kasa özetinin nakit satırından, kart sayılmaz, iptal ve kredi iadesi düşer; İstanbul gün penceresi; tek klinik; Kurumsal dışı red; eski beklenen nakitle red; sunucu değeriyle kayıt), `CashCountsPermissionBindingsTests`, `TenantPlanFeatureEvaluatorTests`.
- Entegrasyon: `DailyTransactionsReportIntegrationTests` (partial `CashCountIntegrationTests.cs`): `day.cash` ≡ aynı gün ve klinikle `cash-summary` nakit satırı (nakit, kart, iptal, satış ile); günlük tek kayıt; sayımdan sonraki tahsilat uyarısı; eski beklenen nakitle düzeltme reddi; düzeltme izi ve geçmiş listesi; iptal; denetim kaydı; Temel'de kayıt 403, okuma 200; izinler; klinik zorunluluğu; başka kiracı.

## 10) Frontend için

1. Gün sonu ekranında (Rapor Merkezi > Gün sonu) seçilen tek gün ve klinik için "Kasa kontrolü" bölümü; `GET /day`.
2. Kasa yönetimi paketinde değilse (Temel; `access-state.planFeatures[CashManagement].isEnabled = false`) form yerine "üst pakete geç" kutusu; gün sonu raporu aynen kalır.
3. Form: açılış (öneri `previous.countedAmount`), sayılan; olması gereken ve fark canlı hesaplanır (gösterim; doğrusu sunucudan döner); fark varsa açıklama zorunlu. Kasa seçimi (`GET /api/v1/cash-registers`, aktif kasa birden fazlaysa); gönderimde `cashRegisterId` ve `expectedCashAmount = day.expectedCashAmount`; giriş/çıkış ve aktarım satırları `movements`ten gösterilir. 409 `CashCounts.ExpectedChanged` gelirse gün yeniden okunur.
4. `current` varsa kayıt özeti (kim, ne zaman), `expectedChangedSinceCount` uyarısı, `CashCounts.Correct` izniyle "Düzelt" ve "İptal et" (gerekçe zorunlu). `history` düzeltme izi.
5. Geçmiş sayımlar listesi `GET /` (son 30 gün varsayılanı istemci kararı).
