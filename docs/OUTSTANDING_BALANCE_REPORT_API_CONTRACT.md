# Borç / açık bakiye raporu API sözleşmesi — Aşama 8, Dilim 4

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-012 (bakiye kuralı), ADR-003 (Rapor Merkezi), `ASAMA8_DISCOVERY.md` bölüm 4 (D-R1, D-R2). İlgili: `CLIENT_BALANCE_API_CONTRACT.md` (tek müşteri bakiyesi), `CHARGES_API_CONTRACT.md`, `PAYMENT_ALLOCATION_API_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme (uygulandı). Salt okuma; **migration yok**, yeni izin yok. Frontend bu belgeye göre ayrı kartta yapılır.

---

## 1) Kararlar (kullanıcı, 2026-10-13)

- **D-R1 — şu anki kalan.** Rapor "bugün itibarıyla" kalan borcu gösterir. Geçmiş bir tarihe göre bakiye ve yaşlandırma kovaları (0-30/31-60 gün) bu sürümde **yok**; bu yüzden **tarih aralığı parametresi yoktur** ve 93 gün sınırı bu rapora uygulanmaz. Yaşlandırma için yalnız ham veri verilir: müşterinin en eski açık ücretinin tarihi (`oldestOpenChargeAtUtc`).
- **D-R2 — yetki `Charges.Read`.** Yeni izin yok. Admin, Owner, ClinicAdmin, Sekreter ve Veteriner görür (müşteri bakiyesi ve Bugün'deki `clientOutstandingAmount` ile aynı kişiler).

## 2) Bakiye kuralı (tek yerde)

Müşterinin kalan tutarı, `GET /clients/{id}/balance` yanıtındaki `outstandingAmount` ve Bugün satırındaki `clientOutstandingAmount` ile **aynı kuraldır**:

- Kaynak `Charge` sayaçlarıdır: ücretin kalanı `Amount − AllocatedAmount` (`Charge.OutstandingAmount`). Bakiye ucu bellekte, rapor SQL toplamı olarak aynı iki sayacı okur; hiçbiri tahsis satırlarını yeniden toplamaz. Eşitlik entegrasyon testiyle korunur (bölüm 9).
- İptal edilmiş ücret (`VoidedAtUtc`) sayılmaz (`Charge` global sorgu filtresi). İptal edilmiş ödemenin tahsisi geri alındığı için ücret yeniden açık görünür; ayrıca bir düzeltme yapılmaz.
- Bağlanmamış (eski) ödemeler borçtan **düşülmez** ve rapora girmez (ADR-012 madde 5, FIN-007).
- Yalnızca TRY.
- Klinik kapsamı `Charges` için kullanılan ortak çözümleyicidir (aşağıda).

Müşteri satırı, kapsamdaki açık ücretlerin (`Amount > AllocatedAmount`) toplamıdır; kalan tutarı 0 olan müşteri rapora girmez.

## 3) Yetki ve kapsam

- Üç uç da policy **`Charges.Read`**. Yoksa 403; token yoksa 401.
- Kiracı bağlamı zorunlu (`Tenants.ContextMissing` 400). Başka kiracının müşteri/ücreti görünmez.
- Klinik kapsamı `payments` raporu ile aynı çözümleyici (`IClinicReadScopeResolver`):
  - `clinicId` verilirse (veya JWT/header `clinic_id` bağlamı varsa) yalnız o kliniğin ücretleri sayılır. İkisi birden verilip farklıysa `OutstandingBalances.ClinicContextMismatch` (400). Kullanıcıya atanmamış klinik → `Clinics.AccessDenied` (403); kiracıda olmayan klinik → `Clinics.NotFound` (404).
  - Bağlam yoksa kullanıcının erişebildiği tüm kliniklerin ücretleri sayılır (Admin/Owner: kiracı geneli). Atanmış klinik yoksa sonuç boştur.
- Aynı `clinicId` ile bir müşterinin `outstandingAmount` değeri Bugün satırındaki `clientOutstandingAmount` ile; bağlamsız çağrıdaki değeri `GET /clients/{id}/balance` (bağlamsız) ile **birebir aynıdır** (entegrasyon testiyle kanıtlanır).
- Okuma Command DB'dedir; Query DB bayrakları bu rapor için geçerli değildir (para verisi eventual-consistent projeksiyondan okunmaz).

## 4) `GET /api/v1/reports/outstanding-balances`

Sorgu parametreleri:

| Parametre | Tip | Not |
|---|---|---|
| `clinicId` | guid? | Klinik kapsamı (bölüm 3) |
| `minAmount` | decimal? | Yalnız kalan tutarı **≥ minAmount** olan müşteriler. Boş = süzgeç yok (kalan > 0 zaten zorunlu). Negatif → `OutstandingBalances.MinAmountInvalid` (400) |
| `search` | string? | Müşteri adı, e-posta veya telefon; günlük arama kuralı (`DailySearchTerm`: Türkçe harf, çok sözcük, telefon biçimi toleransı) |
| `page` | int | Varsayılan 1 |
| `pageSize` | int | Varsayılan 50, en çok 200 (üstü sınırlanır) |

Yanıt `OutstandingBalancesReportResultDto`:

| Alan | Tip | Not |
|---|---|---|
| `currency` | string | Her zaman `TRY` |
| `totalCount` | int | Süzgeçlere uyan **borçlu müşteri sayısı** (tüm sayfalar) |
| `totalOutstandingAmount` | decimal | Süzgeçlere uyan tüm müşterilerin kalan toplamı (yalnız sayfa değil) |
| `items` | `OutstandingBalanceItemDto[]` | Sayfadaki satırlar |

`OutstandingBalanceItemDto`:

| Alan | Tip | Not |
|---|---|---|
| `clientId` | guid | Müşteri sayfasına/bakiyesine bağlantı için |
| `clientName` | string | |
| `phone` | string? | `905XXXXXXXXX` veya null |
| `outstandingAmount` | decimal | Kalan tutar (> 0) |
| `openChargeCount` | int | Açık (kalanı > 0) ücret sayısı |
| `oldestOpenChargeAtUtc` | datetime | En eski açık ücretin `createdAtUtc` değeri (yaşlandırma için ham veri) |

Sıralama: `outstandingAmount` azalan, sonra `clientName`, sonra `clientId` (deterministik; sayfalar kesişmez/atlamaz).

Örnek: A müşterisi 1.000 TL ücret + 400 TL tahsilat, B müşterisi 90 TL ücret → `totalCount 2`, `totalOutstandingAmount 690`, `items[0] = A (600)`, `items[1] = B (90)`. A ikinci 600 TL'yi öderse A listeden çıkar.

## 5) Dışa aktarma

- `GET /api/v1/reports/outstanding-balances/export` (CSV: UTF-8 BOM, `;` ayırıcı, Excel TR) ve `/export-xlsx`.
- Parametreler listedeki ile aynıdır, **`page`/`pageSize` hariç**; tüm eşleşen satırlar gelir. Süzgeç semantiği ve sıralama JSON ile **aynı kod yolundan** geçer; ekran ile dışa aktarma aynı müşteri kümesini ve aynı sırayı verir (testle kanıtlanır).
- Satır tavanı `ReportsSharedLimits.MaxExportRows` (50.000); aşılırsa `OutstandingBalances.ReportExportTooManyRows` (400): "filtreyi daraltın".
- Kolonlar (teknik ID yok): `Müşteri`, `Telefon`, `Kalan Tutar`, `Para Birimi`, `Açık Ücret Sayısı`, `En Eski Açık Ücret` (Europe/Istanbul, `dd.MM.yyyy`). XLSX'te tutar sayı biçimli, tarih tarih biçimli hücredir.
- Dosya adı: `borc-raporu-yyyyMMdd.csv` / `.xlsx` (İstanbul tarihi).
- Toplam satırı dosyaya eklenmez (süzgeç/Excel toplamı kullanıcıdadır); ekrandaki toplam `totalOutstandingAmount` ile gösterilir.

## 6) Hata kodları

| Kod | HTTP |
|---|---|
| `Tenants.ContextMissing` | 400 |
| `OutstandingBalances.MinAmountInvalid` | 400 |
| `OutstandingBalances.ClinicContextMismatch` | 400 |
| `OutstandingBalances.ReportExportTooManyRows` | 400 |
| `Clinics.AccessDenied` | 403 |
| `Clinics.NotFound` | 404 |
| (yetkisiz) | 401 / 403 |

## 7) Uygulama notları

- Tek sorgu: kiracı + klinik kapsamındaki açık ücretler `ClientId` ile gruplanır (`SUM`, `COUNT`, `MIN`), `Clients` ile birleştirilir; sıralama, sayfalama ve toplamlar veritabanında yapılır. N+1 yok, tüm ücretler belleğe alınmaz.
- Okuyucu: `IOutstandingBalancesReportReader` (Application) / `OutstandingBalancesReportReader` (Infrastructure, mevcut `AppointmentsReportStatusBreakdownReader` deseni). JSON ve iki dışa aktarma aynı okuyucu çağrısını kullanır.
- **İndeks:** mevcut `IX_Charges (TenantId, ClientId)` ve `(TenantId, ClinicId, CreatedAtUtc)` ile sorgu kiracının ücretlerini tarar; `Amount > AllocatedAmount` kolon-kolon karşılaştırması olduğu için filtreli indeks kurulamaz. Pilot ölçeğinde (kiracı başına on binlerce ücret) yeterli beklenir; büyük hacimde kapsayıcı indeks (`TenantId, ClientId` + `INCLUDE ClinicId, Amount, AllocatedAmount, CreatedAtUtc, VoidedAtUtc`) migration gerektirir ve **ayrı onayla** eklenir. Bu sürümde indeks eklenmedi.
- Telefon `Clients` alanıdır; rapor `Charges.Read` ister, `Clients.Read` istemez. Rapor yalnız ücreti açık olan müşterilerin adını/telefonunu döner.

## 8) Frontend için (ayrı kart)

Rapor sayfası `Charges.Read` ile görünür (Rapor Merkezi kartı izin kontrolü bunu kullanmalı; `Payments.Read` yetmez). Sayfa: kalan toplamı + borçlu müşteri sayısı (`totalCount`, `totalOutstandingAmount`), "en az tutar" ve arama süzgeçleri, tablo (müşteri adı → müşteri/bakiye bağlantısı, telefon, kalan, açık ücret sayısı, en eski açık ücret tarihi), sayfalama, CSV/XLSX düğmeleri (aynı süzgeçler). Tarih seçici **yok**; "şu anki kalan" ifadesi ekranda açıkça yazılmalı (bakiye anlık görüntüdür).

## 9) Test odağı

- Application: kiracı/klinik/min tutar/arama doğrulamaları, bağlam uyuşmazlığı, dışa aktarma tavanı, CSV/XLSX içerik ve biçim.
- Entegrasyon (LocalDB, kendi kiracısıyla): sıralama + toplamlar + sayfalama, iptal edilmiş ücret ve ödeme, kısmi/tam ödeme, `outstandingAmount` ≡ `GET /clients/{id}/balance` ve Bugün `clientOutstandingAmount`, ekran ≡ CSV ≡ XLSX, klinik kapsamı, başka kiracı (IDOR), `Charges.Read` olmadan 403, token olmadan 401.
