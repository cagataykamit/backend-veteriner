# Çoklu kasa ve nakit yönetimi API sözleşmesi — TIER-008

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-008`, `FIN-002`, ADR-012 (K2 düzeltme yetkisi), ADR-035 (2026-11-09
kullanıcı kararı: kasa yönetimi Profesyonel ve Kurumsal'da). İlgili: `CASH_COUNT_API_CONTRACT.md` (kasa bazında sayım),
`DAILY_TRANSACTIONS_REPORT_API_CONTRACT.md` (kasa özeti, değişmedi), `CLIENT_CREDIT_API_CONTRACT.md` (ödeme ve iade gövdesi),
`PLAN_FEATURES_API_CONTRACT.md`, `DATA_EXPORT_API_CONTRACT.md`.

> **Durum:** Uygulandı (kod + test), dal `feat/tier-008-coklu-kasa`. Migration `AddCashRegisters` (yalnız yerel DB). **Yeni izinler**
> `CashRegisters.Read`, `CashRegisters.Manage`, `CashMovements.Create`, `CashMovements.Void`. §9'daki maddeler **çalışma varsayımıdır**.
> Gün sonu raporları (döküm, kasa özeti) her pakette aynen kalır.

---

## 1) Kavramlar

| Kavram | Kural |
|---|---|
| Kasa | Klinik başına; `code` (klinikte benzersiz, büyük harfe çevrilir, en çok 20), `name` (en çok 100), `isActive`. Silinmez. |
| Ana kasa | Her klinikte tek (`isMain`), kod `ANA`, ad "Ana kasa" (ad değişebilir). **Pasife alınamaz** (`CashRegisters.MainRegisterLocked`, 409). Yeni klinik ve yeni kiracı Ana kasayla açılır; uygulama yolu dışında açılmış klinikte ilk kasa yazımında oluşturulur. |
| Kasası boş nakit | Kasası belirtilmemiş nakit tahsilat ve nakit kredi iadesi (eski kayıt, örnek veri) **kliniğin Ana kasasınındır**. Okumaların tek kuralı (`FinanceEvents` kasa filtresi). |
| Pasif kasa | Yeni hareket (tahsilat, iade, giriş/çıkış, aktarım, sayım) girilmez (`CashRegisters.Inactive`, 409); geçmişi okunur, düzeltme ve iptal yapılabilir. |
| Kart, havale | Kasaya bağlanmaz (banka hesapları bu dilimde yok). Kart/havale ödemesine `cashRegisterId` verilirse `Payments.Validation` (400). |
| Gelir/gider türü | Kiracı başına küçük liste (`CashMovementCategory`): ad (kiracıda benzersiz, en çok 100), `isActive`. Varsayılanlar: Bankaya yatırma, Masraf, Bozuk para, Diğer. Silinmez; pasif tür yeni harekette seçilemez. |
| Kasa hareketi | Giriş (`In`), çıkış (`Out`), aktarım bacakları (`TransferIn`, `TransferOut`). Tutar > 0, en çok 10.000.000, en çok 2 ondalık, TRY. İleri tarih girilemez; geçmiş gün girilebilir. |
| Aktarım | Aynı klinikteki iki aktif kasa arasında; tek işlemde iki bacak (`transferId` ortak). İptali iki bacağı birlikte iptal eder. Başka klinikteki kasa bulunmaz (404). |
| Düzeltme | Kayıt ezilmez: gerekçeli iptal (5–500 karakter, `CorrectionReason`) ve yeni kayıt. İptal edilen hareket bakiyeye girmez; kaydı kalır. |

## 2) Beklenen nakit ve bakiye (tek kural: `CashRegisterTotalsDto`)

```
netAmount = cashNetAmount + inAmount − outAmount + transferInAmount − transferOutAmount
cashNetAmount = nakit tahsilat − nakit tahsilat iptali − nakit kredi iadesi   (kasa filtreli kasa özeti nakit satırı)
```

- Gün sonu kasa sayımında beklenen nakit = kasanın gün `netAmount`'u (`CASH_COUNT_API_CONTRACT.md` §1).
- Kasa hareketi olmayan Ana kasada ve tek kasalı klinikte sonuç **gün sonu kasa özetinin nakit satırıyla aynıdır** (entegrasyon testi).
- Bütün kasaların `cashNetAmount` toplamı kasa özetinin nakit satırına eşittir; aktarımlar klinik toplamını değiştirmez.
- Rapor bakiyesi aynı toplamla yürür: devreden = dönemden önceki tüm hareketlerin `netAmount`'u (başlangıçtan; sayım farkı bakiyeye girmez).

## 3) Uçlar

Kiracı bağlamı zorunlu. **Tek klinik zorunlu** (kategoriler hariç): `clinicId` ya da aktif klinik bağlamı; çözümleme gün sonu raporlarıyla aynı
bileşenden. Klinik yoksa `{Modül}.ClinicScopeRequired` (400); atanmamış klinik `Clinics.AccessDenied` (403); başka kiracı/klinik kasası
`CashRegisters.NotFound` (404).

| Uç | İzin | Paket |
|---|---|---|
| `GET /api/v1/cash-registers?clinicId=&includeInactive=false` → `CashRegisterDto[]` (Ana kasa önce) | `CashRegisters.Read` | Her paket |
| `POST /api/v1/cash-registers` `{ clinicId?, code, name }` → `CashRegisterDto` | `CashRegisters.Manage` | Pro, Premium |
| `PUT /api/v1/cash-registers/{id}` `{ code, name, isActive }` → `CashRegisterDto` | `CashRegisters.Manage` | Pro, Premium |
| `GET /api/v1/cash-registers/report?from=&to=&clinicId=&cashRegisterId=` → `CashRegisterReportDto` | `CashRegisters.Read` | Her paket |
| `GET /api/v1/cash-registers/report/export` (CSV), `/report/export-xlsx` (XLSX), aynı filtreler | `CashRegisters.Read` | Her paket |
| `GET /api/v1/cash-movements?from=&to=&clinicId=&cashRegisterId=&includeVoided=false&page=1&pageSize=50` → `PagedResult<CashMovementDto>` | `CashRegisters.Read` | Her paket |
| `POST /api/v1/cash-movements` → `CashMovementDto` | `CashMovements.Create` | Pro, Premium |
| `POST /api/v1/cash-movements/transfers` → `CashMovementDto` (çıkış bacağı) | `CashMovements.Create` | Pro, Premium |
| `POST /api/v1/cash-movements/{id}/void` `{ reason }` → 204 | `CashMovements.Void` | Pro, Premium |
| `GET /api/v1/cash-movements/categories?includeInactive=false` → `CashMovementCategoryDto[]` | `CashRegisters.Read` | Her paket |
| `POST /api/v1/cash-movements/categories` `{ name }`, `PUT /categories/{id}` `{ name, isActive }` | `CashRegisters.Manage` | Pro, Premium |

Pakette yoksa 403 `Subscriptions.FeatureNotInPlan`, `requiredPlanCode: "Pro"`. Okumalar paket düşünce de açıktır (kasalar ve hareketler silinmez).
Tarih aralığı en çok 366 gün (`Registers.DateRangeInvalid` / `Registers.RangeTooLong`).

### 3.1 `CashRegisterDto`
`id`, `clinicId`, `code`, `name`, `isMain`, `isActive`.

### 3.2 `POST /cash-movements`
```json
{ "clinicId": "…", "cashRegisterId": "…", "direction": "Out", "businessDate": "2026-11-09", "amount": 1500.00,
  "categoryId": "…", "description": "Akşam bankaya yatırıldı" }
```
`direction`: `In` | `Out` (metin). `categoryId` zorunlu ve aktif olmalı. Hatalar: `CashMovements.Validation` (400), `CashMovementCategories.NotFound`
(404), `CashRegisters.NotFound` (404), `CashRegisters.Inactive` (409).

### 3.3 `POST /cash-movements/transfers`
```json
{ "clinicId": "…", "sourceCashRegisterId": "…", "targetCashRegisterId": "…", "businessDate": "2026-11-09", "amount": 300.00, "description": null }
```
Kaynak = hedef ya da farklı klinik: `CashMovements.Validation` (400) / `CashRegisters.NotFound` (404). İki bacak tek `SaveChanges`'tedir.

### 3.4 `CashMovementDto`
`id`, `clinicId`, `cashRegisterId`, `cashRegisterName`, `kind` (`In|Out|TransferIn|TransferOut`), `businessDate`, `amount`, `currency`,
`categoryId`, `categoryName`, `description`, `transferId`, `counterpartCashRegisterId`, `counterpartCashRegisterName`, `createdByUserId`,
`createdByName`, `createdAtUtc`, `isVoided`, `voidedAtUtc`, `voidedByName`, `voidReason`. Sıra: gün, sonra kayıt zamanı azalan.

### 3.5 `POST /cash-movements/{id}/void`
Aktarımın herhangi bir bacağı verilirse iki bacak birlikte iptal edilir (biri iptalliyse hiçbiri değişmez). Hatalar: `CashMovements.NotFound` (404;
aktif klinik bağlamı dışı dahil), `CashMovements.AlreadyVoided` (409), `CashMovements.Validation` (400), `CashMovements.ConcurrencyConflict` (409).

### 3.6 `CashRegisterReportDto`
`clinicId`, `cashRegisterId` (boşsa klinikteki tüm kasalar), `cashRegisterName`, `from`, `to`, `currency`, `openingBalance` (devreden), `totals`
(`CashRegisterTotalsDto`: `cashNetAmount`, `inAmount`, `outAmount`, `transferInAmount`, `transferOutAmount`, `netAmount`), `closingBalance`
(= devreden + `totals.netAmount`), `rows[]`:

| Alan | Not |
|---|---|
| `businessDate`, `atUtc` | Tahsilat/iptal/iade olay anı; kasa hareketinde kayıt anı (geçmiş güne girilmişse o günün başı) |
| `kind` | `Collection`, `CollectionVoid`, `CreditRefund`, `In`, `Out`, `TransferIn`, `TransferOut` |
| `referenceId` | Ödeme, kredi ya da kasa hareketi kimliği |
| `cashRegisterId`, `cashRegisterName` | Kasası boş nakitte Ana kasa adı |
| `description` | Müşteri adı (+ iptal/iade gerekçesi); tür adı ya da "Hedef/Kaynak: kasa" (+ açıklama) |
| `inAmount`, `outAmount`, `balance` | Yürüyen bakiye satırdan sonradır; son satırın bakiyesi `closingBalance` |
| `performedByName` | Tahsilatta alan, iptal/iadede yapan, harekette kaydeden. Tüm satırlarda aynı kural (`UserDisplayName`): görünen ad, yoksa e-postanın `@` öncesi (e-postanın tamamı yazılmaz) |

İptal edilmiş kasa hareketleri rapora girmez; tahsilat iptali (gün sonu kuralı) iptal anında eksi satırdır. Satır sınırı 50.000
(`CashRegisters.TooManyRows`, 400). Dosya: Tarih (iş günü), Tarih ve saat (`atUtc`, İstanbul `dd.MM.yyyy HH:mm`; eski adı "Saat"), Kasa, Hareket, Açıklama, Giriş, Çıkış, Bakiye, İşlemi Yapan; ilk satır "Devreden
bakiye", son satır "Dönem sonu bakiye"; ad `kasa-hareketleri-{damga}.csv|xlsx` (rapor deseni).

## 4) Ödemede ve iadede kasa (mevcut uçlar, geriye uyumlu)

| Uç | Eklenen alan |
|---|---|
| `POST /api/v1/payments` | `cashRegisterId?` |
| `POST /api/v1/sales` → `payment` | `cashRegisterId?` |
| `POST /api/v1/clients/{clientId}/credits/{creditId}/refund` | `cashRegisterId?` |

- Yalnız nakitte anlamlıdır. Verilmezse alan boş kalır ve okuma kuralıyla kliniğin Ana kasasıdır; eski istek değişmeden ve ek sorgu olmadan çalışır.
- Verilen kasa aynı klinikte ve aktif olmalı (`CashRegisters.NotFound` 404, `CashRegisters.Inactive` 409). Ana kasa dışındaki kasa
  `CashManagement` ister (Temel'de ya da paket düşünce 403); Ana kasa her pakette.
- `requestId` parmak izine `cashRegisterId` yalnız verildiğinde girer (eski kayıtların tekrar isteği bozulmaz).
- Ödeme düzeltmesinde (`PUT /payments/{id}`) yöntem nakitten çıkarsa ya da klinik değişirse kasa bağı kalkar (nakitse Ana kasaya düşer). Kasa
  değiştirme bu dilimde yok; yanlış kasa iptal ve yeni ödemeyle düzeltilir.
- Ödeme iptali ve kredi iadesi kendi kasasından düşer.

**Okumada kasa (2026-11-09, geriye uyumlu, isteğe bağlı alanlar):** `GET /api/v1/payments/{id}` (`PaymentDetailDto`) ve `GET /api/v1/payments`
(`PaymentListItemDto`) yanıtlarına `cashRegisterId?`, `cashRegisterName?` eklendi. Yalnız nakit ödemede doludur: kayıtlı kasa, kasası boşsa
kliniğin Ana kasası (Ana kasa kaydı yoksa `cashRegisterId` boş, ad "Ana kasa"). Kart ve havalede ikisi de `null`. Kural tek yerde
(`CashRegisterRules.LoadForPaymentsAsync`, sayfa başına tek toplu sorgu). Query DB okuma yolunda (bayrak kapalı) `null`. Pasif kasanın adı da
yazılır. Ek izin gerekmez (`Payments.Read`).

## 5) Denetim

Yazma komutları `IAuditableRequest`; başarılı ve başarısız her çağrı `AuditLogs`'a yazılır.

| `Action` | `TargetId` |
|---|---|
| `CashRegister.Create` | `ClinicId=…` (istekteki; aktif bağlamdan geliyorsa boş) |
| `CashRegister.Update` | `CashRegisterId=…` |
| `CashMovement.Create` | `CashRegisterId=…` |
| `CashTransfer.Create` | `SourceCashRegisterId=…;TargetCashRegisterId=…` |
| `CashMovement.Void` | `CashMovementId=…` |
| `CashMovementCategory.Create` / `.Update` | — / `CashMovementCategoryId=…` |

Gün sonu dökümüne (`daily-transactions`) kasa hareketi yazılmaz: döküm müşteri tahsilatı ve ücret olaylarıdır; kasa hareketleri bu rapordadır.

## 6) Veri modeli (migration `AddCashRegisters`)

- `CashRegisters`: `Id`, `TenantId`, `ClinicId` (FK restrict), `Code nvarchar(20)`, `Name nvarchar(100)`, `IsMain`, `IsActive`, `CreatedAtUtc`,
  `UpdatedAtUtc`. Benzersiz `(TenantId, ClinicId, Code)`; benzersiz `(TenantId, ClinicId)` `IsMain = 1` filtreli. `CHECK`: Ana kasa aktif.
- `CashMovementCategories`: `Id`, `TenantId`, `Name nvarchar(100)`, `IsActive`, `CreatedAtUtc`, `UpdatedAtUtc`. Benzersiz `(TenantId, Name)`.
- `CashMovements`: `Id`, `TenantId`, `ClinicId`, `CashRegisterId`, `Kind int`, `BusinessDate date`, `Amount decimal(18,2)`, `Currency`,
  `CategoryId?`, `Description nvarchar(500)`, `TransferId?`, `CounterpartCashRegisterId?`, `CreatedByUserId`, `CreatedAtUtc`, üç iptal alanı,
  `MutationSequence` (concurrency). `CHECK`: tutar > 0; iptal alanları birlikte; giriş/çıkışta aktarım alanları boş, aktarımda dolu.
  İndeks `(TenantId, ClinicId, CashRegisterId, BusinessDate)`, `(TenantId, TransferId)` filtreli.
- `Payments.CashRegisterId?`, `ClientCredits.RefundCashRegisterId?` (FK restrict). `CashCounts.CashRegisterId` (zorunlu, FK); tek geçerli sayım
  indeksi `UX_CashCounts_TenantId_ClinicId_CashRegisterId_BusinessDate_Active`.
- **Geriye dönük doldurma** (`CashRegisterBackfill.Sql`, tekrar çalıştırılabilir): Ana kasası olmayan her kliniğe Ana kasa; kasası boş nakit
  ödemeler, nakit kredi iadeleri ve kasa sayımları Ana kasaya; hiç türü olmayan kiracıya varsayılan türler. Dolu alana dokunmaz.
- Kiracı filtresi `ITenantOwned` (ADR-040). Paylaşılan/üretim DB'ye uygulanması kullanıcı onayı ister.

## 7) Yetkiler

| İzin | Varsayılan roller |
|---|---|
| `CashRegisters.Read` (kasa, tür, hareket listesi, rapor) | Admin, Owner, ClinicAdmin, Sekreter |
| `CashRegisters.Manage` (kasa ve tür tanımı) | Admin, Owner, ClinicAdmin |
| `CashMovements.Create` (giriş/çıkış, aktarım) | Admin, Owner, ClinicAdmin, Sekreter |
| `CashMovements.Void` (gerekçeli iptal) | Admin, Owner, ClinicAdmin |

Veteriner almaz. Sayım izinleri (`CashCounts.*`) değişmedi. İzinler `RolePermissionBindings` ile seed'de idempotent eklenir; **mevcut özel roller
elle almalı** (deploy notu). Ödeme alma ekranı kasa listesi için `CashRegisters.Read` ister; izin yoksa istemci kasa sormaz (Ana kasa).

## 8) Paket kilidi (`PlanFeature.CashManagement`: Pro, Premium)

- Temel: kasa tanımı, Ana kasa dışında kasa seçimi, giriş/çıkış, aktarım ve sayım kaydı kapalı (403). Görünmeyen tek Ana kasa ile ödeme, iade ve
  gün sonu bugünkü gibi çalışır.
- Paket düşünce kasalar, hareketler ve sayımlar silinmez, okunur; yeni nakit işlem yalnız Ana kasaya.
- Denemede seçilen pakete göre davranır.

## 9) Çalışma varsayımları

1. Gelir/gider türleri kiracı geneli (tüm klinikler aynı liste); klinik başına liste sonraki dilim.
2. Aktarım yalnız aynı klinikteki kasalar arasında.
3. İptal edilen kasa hareketi hiç olmamış sayılır (girildiği günden düşer); sonradan değişen gün sayımı `expectedChangedSinceCount` ile uyarır.
4. Devreden bakiye sistemin başından beri tüm kasa hareketlerinin netidir; sayım farkı bakiyeyi düzeltmez (fark kasaya giriş/çıkışla işlenir).
5. Pasif kasaya sayım girilmez; eski sayım düzeltilip iptal edilebilir.
6. Ödemenin kasası sonradan değiştirilemez (iptal + yeni ödeme).
7. Kasa listesi, rapor ve hareket listesi her pakette okunur; panel Temel'de bunları göstermez, yalnız "üst pakete geç" kutusu (paket düşmüşse
   kayıtlar raporda okunur).

## 10) Testler

- Domain: `CashRegisterTests` (Ana kasa kilidi, kod/ad, giriş/çıkış ve tutar kuralları, pasif kasa, aktarım bacakları, iptal, varsayılan türler),
  `PaymentCashRegisterTests` (yalnız nakit, yöntem/klinik değişince bağ kalkar), `CashCountTests`.
- Application: `CashCountHandlerTests` (kasa hareketleri beklenen nakde eklenir, Ana kasa filtresi, pasif kasa, Pro ve Premium),
  `CashRegistersPermissionBindingsTests`, `TenantPlanFeatureEvaluatorTests`.
- Entegrasyon (`Reports/CashRegisterIntegrationTests.cs`, `CashCountIntegrationTests.cs`): tek kasalı klinikte beklenen = kasa özeti nakit satırı;
  çok kasada kasa bazında beklenen nakit ve toplamın kasa özetine eşitliği; giriş/çıkış; aktarım ve iptalinin iki bacağı; pasif kasa ve Ana kasa
  kilidi; Temel'de 403 ve Ana kasa ile ödeme; `cashRegisterId`'siz eski ödeme; kiracı ve klinik izolasyonu; yetki; rapor devreden bakiyesi ve
  CSV/XLSX; geriye dönük doldurmanın iki kez çalıştırılması; denetim kaydı. `DataExportIntegrationTests` sözleşme dosya listesi eşleşmesi.
