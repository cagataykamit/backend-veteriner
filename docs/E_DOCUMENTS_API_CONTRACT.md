# e-belge (e-SMM / e-Arşiv / e-Fatura) API sözleşmesi (ADR-036 Dilim 3–4)

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-036 (vetinity-product, kabul edildi) — Dilim 3 (e-belge çekirdeği), Dilim 4 (kes / iptal / PDF), AK-1 (a) klinik kendi entegratör hesabını bağlar, AK-3 (c), AK-4 v1, AK-6. İlk sağlayıcı **Paraşüt** (AK-2, 2026-11-11). Uçlar `api/v1` altında (OAuth dönüşü hariç, §3.3); hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme + uygulama (dal `feat/e-belge-d3-d4-parasut`). Migration `AddEDocuments` (20261110234342) yalnız yerel geliştirme veritabanında uygulandı; paylaşılan/üretim veritabanına uygulanmadı.
> **Kullanıcı kararları (2026-11-11):** ödeme ekranında "e-Belge kes", belgesiz ücret hatırlatması (menü rozeti, gün sonu bildirimi) ve tam otomatik kesme (AK-4 d, v2'den v1'e) bu işe girdi (§4.8–§4.10, §6). e-belge ekranları ve uçları sağlayıcıdan bağımsızdır; sağlayıcıya özgü her şey sağlayıcı uygulamasında kalır.
> **Kapsam dışı:** iade belgesi, gelen e-Fatura, webhook (v2); raporlar ve canlı ortam geçişi (Dilim 5).

---

## 1) Kavramlar ve kurallar (tek yer)

| Kural | Yer |
|---|---|
| Belge türü: klinik mükellef türü × satır türü × alıcı e-Fatura mükellefi mi → `ESmm` / `EArchive` / `EInvoice` | `Domain/EDocuments/EDocumentTypeRule` |
| İptal süresi (gün), bilinmeyen alıcı kimliği, stopajın yalnız kurumsal alıcıya uygulanması, belge başına en çok ücret | `Domain/EDocuments/EDocumentRules` (mevzuat **DOĞRULANMADI**, değer tek yerde) |
| Ücret tutarları **KDV dahildir** (müşterinin ödediği tutar); belgeye KDV hariç birim fiyat gönderilir | `Domain/EDocuments/EDocumentAmounts` |
| Durum makinesi | `Domain/EDocuments/EDocument` |
| Bir ücret yalnız bir geçerli belgeye bağlı | `Charge.EDocumentId` (tek sütun) + `Charge` eşzamanlılık belirteci |

### 1.1 Belge türü kuralı

| Klinik mükellef türü (`ClinicTaxpayerSettings.taxpayerType`) | Hizmet satırı | Ürün satırı |
|---|---|---|
| `SelfEmployed` (serbest meslek) | e-SMM | e-SMM |
| `Company` (şirket) | fatura | fatura |
| `Both` (karma) | e-SMM | fatura |
| null | — `EDocuments.TaxpayerTypeMissing` (400) | |

"Fatura" = alıcı e-Fatura mükellefiyse **e-Fatura**, değilse **e-Arşiv**. Mükellef sorgusu yalnız kurumsal alıcıda (`Client.personType = Corporate` ve `taxNumber` dolu) sağlayıcıdan yapılır; bireysel/bilinmeyen alıcı e-Arşiv'dir. Satış (`Sale`) satırları her zaman ürün satırıdır (satışın ücreti hangi satır türüyle yazılmış olursa olsun).

Bir belge tek türdür. Seçilen ücretler iki türe ayrılıyorsa önizleme gruplar (§4.1), kesme isteği tek grup ister (`EDocuments.MixedDocumentTypes`, 400).

### 1.2 Alıcı

Alıcı müşterinin fatura kimliğinden (`INVOICING_IDENTITY_API_CONTRACT.md` §2) belge anında kopyalanır (belgede saklanır, sonradan müşteri değişse de belge değişmez):

- Kurumsal: `legalName` (yoksa `fullName`), `taxNumber` **zorunlu**, `taxOffice` önerilir. Vergi numarası yoksa `EDocuments.BuyerIdentityMissing` (400, `extensions.missingFields`).
- Bireysel veya kişi türü boş: ad soyad; TCKN yoksa **bilinmeyen alıcı** kimliği `11111111111` kullanılır (DOĞRULANMADI, `EDocumentRules.UnknownBuyerNationalId`).
- Peşin müşteri (ADR-016): bilinmeyen alıcı.
- Adres: `billingProvince`, `billingDistrict`, `billingAddressLine` (boşsa klinik adresi değil, boş gönderilir).

### 1.3 Tutar

Ücret tutarı KDV dahildir. Satır KDV oranı `Charge.vatRate ?? ClinicTaxpayerSettings.defaultVatRate` (kayıt yoksa %20). Stopaj yalnız e-SMM'de ve alıcı kurumsalsa `ClinicTaxpayerSettings.withholdingRate` ile uygulanır (DOĞRULANMADI). KDV hariç satır tutarı `brüt / (1 + kdv − stopaj)`, 2 ondalığa yuvarlanır; birim fiyat bu tutarın miktara bölümüdür (en çok 6 ondalık). Sağlayıcının yuvarlaması nedeniyle belge toplamı ücret toplamından en çok birkaç kuruş farklı olabilir; belge toplamı sağlayıcıdan okunup `providerTotal` alanında döner.

---

## 2) Durum makinesi

```
Draft → Sending → Issued
            ↘ Failed → (retry) → Sending
            ↘ Failed → (discard) → Discarded
Issued → CancelRequested → Cancelled
                         ↘ CancelRejected → (cancel tekrar) → CancelRequested
```

| Durum | Anlamı | Ücretler bağlı mı |
|---|---|---|
| `Draft` | Kayıt yazıldı, gönderim başlamadı (yalnız istek içinde görülür) | Evet |
| `Sending` | Sağlayıcıya gönderildi, sonuç bekleniyor (asenkron iş veya geçici hata sonrası yeniden deneme) | Evet |
| `Issued` | Kesildi; belge no / ETTN var | Evet |
| `Failed` | Kalıcı hata (veri, yetki, kontör…); kullanıcı düzeltip yeniden dener veya vazgeçer | Evet |
| `Discarded` | Hatalı belgeden vazgeçildi; sağlayıcıda taslak fatura varsa iptal edilir | **Hayır** (ücretler serbest) |
| `CancelRequested` | İptal sağlayıcıya iletiliyor | Evet |
| `Cancelled` | İptal edildi | **Hayır** |
| `CancelRejected` | Sağlayıcı iptali reddetti; belge kesilmiş kalır | Evet |

- Kesilmiş belge değiştirilmez; düzeltme = iptal + yeni belge (iptal süresi içinde).
- Belgeye bağlı ücret **düzeltilemez ve iptal edilemez** (`Charges.EDocumentAttached`, 409): önce belge iptal edilir (ADR-036 "belge iptali önerilir").
- Geçici hata (ağ, 429, 5xx) belgeyi `Sending`'de tutar; arka plan işi artan aralıkla en çok `EDocuments:Processor:MaxAttempts` kez dener, sonra `Failed`.

---

## 3) Sağlayıcı bağlantısı (klinik başına)

Klinik başına tek bağlantı (Tenant + Clinic benzersiz). Kimlik bilgileri (OAuth erişim/yenileme belirteçleri) **ASP.NET Data Protection ile şifreli** saklanır, hiçbir yanıtta dönmez, günlüğe yazılmaz. İzin: okuma `Clinics.Read`, yazma `Clinics.Update` (Admin, Owner, ClinicAdmin). Atanmamış klinik `Clinics.AccessDenied` (403).

### 3.1 `GET /clinics/{clinicId}/e-document-connection`

```json
{
  "clinicId": "…",
  "provider": "parasut",
  "providerName": "Paraşüt",
  "environment": "Test",
  "status": "Connected",
  "accountId": "123456",
  "accountName": "Örnek Klinik",
  "connectedAtUtc": "2026-11-11T10:00:00Z",
  "lastVerifiedAtUtc": "2026-11-11T10:00:05Z",
  "lastVerificationOk": true,
  "lastVerificationMessage": "Bağlantı çalışıyor.",
  "accountChoices": null,
  "availableProviders": [ { "provider": "parasut", "providerName": "Paraşüt", "configured": true, "connectionMethod": "OAuth" } ]
}
```

- `status`: `NotConnected` (kayıt yok veya koparıldı) | `Connected` | `AccountSelectionRequired` (hesapta birden çok firma var, §3.4) | `ReauthorizationRequired` (yenileme belirteci geçersiz; yeniden bağlanmalı).
- `environment`: `Test` | `Live` — sunucu ayarından (`EDocuments:Parasut:Environment`); belgeye de yazılır. Test ortamında kesilen belge gerçek değildir.
- `accountChoices`: yalnız `AccountSelectionRequired` iken `[{ "accountId", "accountName", "taxNumber" }]`.
- `connectionMethod`: `OAuth` (sağlayıcının ekranında onay, §3.2) | `Credentials` (ileride anahtar/kullanıcı alanlarıyla bağlanan sağlayıcılar). Panel sağlayıcı listesini ve kartı buna göre çizer; belge akışlarında sağlayıcı adı geçmez.
- `configured: false` = sunucuda uygulama kimliği yok; bağlanma `EDocuments.ProviderNotConfigured` (503) döner.

### 3.2 `POST /clinics/{clinicId}/e-document-connection/authorize`

Gövde `{ "provider": "parasut" }`. Yanıt `{ "authorizationUrl": "https://id-staging.parasut.com/…" }`. Panel tarayıcıyı bu adrese yönlendirir. Adres OAuth 2.0 yetki kodu + PKCE (S256) içerir; `state` sunucunun Data Protection ile şifrelediği, 15 dakika geçerli değerdir (kiracı, klinik, kullanıcı, PKCE doğrulayıcı). Sunucu tarafında bekleyen kayıt tutulmaz.

### 3.3 `GET /api/e-documents/parasut/callback` (sürümsüz, anonim)

Paraşüt'e kayıtlı kalıcı yönlendirme adresi; **`api/v1` altında değildir**: `https://localhost:7173/api/e-documents/parasut/callback` (yerel), `https://api.vetinity.com/api/e-documents/parasut/callback` (canlı). Sorgu `code`, `state` (veya `error`). İşlem: `state` çözülür ve süresi denetlenir → kod belirteçle değiştirilir → `GET /me?include=companies` → tek firma varsa (veya klinik VKN'si tek firmayla eşleşiyorsa) bağlanır, değilse `AccountSelectionRequired` → bağlantı sınanır. Sonunda tarayıcı panele döner:

`{App:FrontendBaseUrl}/panel/settings/e-documents?eDocumentConnection={connected|select-account|error}&clinicId={klinik}&reason={kod}`

(Panelde tek "e-Belge" ayar sayfası; `clinicId` state çözülemezse yoktur.)

`reason` yalnız hata kodudur (ör. `denied`, `expired`, `exchange-failed`); belirteç veya kişisel veri taşımaz. Denetim kaydı: `Clinic.EDocumentConnection.Connect`.

### 3.4 `PUT /clinics/{clinicId}/e-document-connection/account`

Gövde `{ "accountId": "123456" }`; yalnız `AccountSelectionRequired` iken. Seçilen firma bağlanır ve sınanır. Yanıt §3.1.

### 3.5 `POST /clinics/{clinicId}/e-document-connection/verify`

Bağlantıyı sınar (belirteç yenilenir, firma erişilebilir mi, e-belge kullanımı açık mı). Yanıt §3.1 (`lastVerification*` güncel). Bağlantı yoksa `EDocuments.NotConnected` (409).

### 3.6 `DELETE /clinics/{clinicId}/e-document-connection`

Bağlantıyı koparır: şifreli kimlik bilgisi silinir, durum `NotConnected`. Kesilmiş belgeler kalır (PDF/iptal için yeniden bağlanmak gerekir). 204. Denetim: `Clinic.EDocumentConnection.Disconnect`.

---

### 3.7 Klinik e-Belge ayarı — `GET` / `PUT /clinics/{clinicId}/e-document-settings`

Okuma `Clinics.Read`, yazma `Clinics.Update`. Bağlantıdan ayrıdır (koparılınca ayar kalır). Kayıt yoksa varsayılan döner.

```json
{
  "clinicId": "…",
  "autoIssueMode": "Off",
  "endOfDayTime": "23:00",
  "issueOnPaymentByDefault": false,
  "autoIssueEnabledAtUtc": null,
  "taxpayerType": "Both",
  "serviceDocument": "ESmm",
  "productDocument": "Invoice"
}
```

- `autoIssueMode`: `Off` | `OnPayment` (tamamen ödenen ücret arka plan işinin bir sonraki turunda, en çok `EDocuments:Processor:AutomationIntervalSeconds` gecikmeyle kesilir) | `EndOfDay` (günde bir kez `endOfDayTime`'da, İstanbul saati).
- `autoIssueEnabledAtUtc`: otomatik kesmenin açıldığı an; **yalnız bundan sonra yazılan ücretler** otomatik kesilir (açmak geçmişi toplu kesmez). Kapatınca boşalır.
- `issueOnPaymentByDefault`: ödeme ekranındaki "e-Belge kes" seçeneğinin varsayılanı.
- `serviceDocument` / `productDocument`: mükellef türünden çıkan belge (`ESmm` | `Invoice` = alıcıya göre e-Arşiv / e-Fatura); mükellef türü yoksa null. Kural backend'de (§1.1), panel yalnız gösterir.
- PUT gövdesi: `{ autoIssueMode, endOfDayTime: "SS:dd", issueOnPaymentByDefault }`. Geçersiz saat `EDocuments.Validation` (400). Denetim: `Clinic.EDocumentSettings.Update`.

## 4) Belgeler

İzinler: `EDocuments.Read` (Admin, Owner, ClinicAdmin, Sekreter, Veteriner), `EDocuments.Issue` (Admin, Owner, ClinicAdmin, Sekreter), `EDocuments.Cancel` (Admin, Owner, ClinicAdmin). Yeni izinler seed ile gelir; özel roller elle almalı. Klinik kapsamı: istek kliniği aktif klinik bağlamıyla çelişemez; atanmamış klinik 403. Deneme (trial) kiracısında belge kesilemez (`Trial.EDocumentsNotAllowed`, 422; ADR-036 ilke 7).

### 4.1 `POST /e-documents/preview`

Kaynak tam olarak biri: `chargeIds` (1–50), `saleId` veya `visitId` (ziyaretin belgesiz, iptal edilmemiş ücretleri).

```json
{ "clinicId": "…", "chargeIds": ["…"], "saleId": null, "visitId": null }
```

Yanıt:

```json
{
  "clientId": "…",
  "clientName": "Ayşe Yılmaz",
  "buyer": { "kind": "Individual", "name": "Ayşe Yılmaz", "identityMasked": "100******46", "isUnknownBuyer": false, "taxOffice": null },
  "missingBuyerFields": [],
  "connection": { "status": "Connected", "environment": "Test", "providerName": "Paraşüt" },
  "groups": [
    { "documentType": "ESmm", "chargeIds": ["…"], "totalAmount": 1500.00, "lineCount": 2 },
    { "documentType": "EArchive", "chargeIds": ["…"], "totalAmount": 300.00, "lineCount": 1 }
  ],
  "skipped": [ { "chargeId": "…", "reason": "AlreadyDocumented" } ]
}
```

- `skipped.reason`: `AlreadyDocumented` | `Voided`. Hepsi atlanırsa `groups` boş.
- e-Fatura mükellef sorgusu önizlemede de yapılır (bağlantı varsa); bağlantı yoksa kurumsal alıcının fatura grubu `EArchive` görünür ve `connection.status` `NotConnected` olur.
- Tüm ücretler aynı müşteriye ve aynı kliniğe ait olmalı (`EDocuments.MixedClients`, 400).

### 4.2 `POST /e-documents`

```json
{ "requestId": "…", "clinicId": "…", "chargeIds": ["…"], "saleId": null, "visitId": null, "note": null }
```

- `requestId` zorunlu; aynı kiracıda aynı `requestId` ilk belgeyi döndürür (200, yeniden gönderim yapmaz). Farklı içerikle aynı `requestId` → `EDocuments.RequestIdConflict` (409).
- Seçim tek gruba düşmeli (§1.1). Ücret başka geçerli belgeye bağlıysa `EDocuments.ChargeAlreadyDocumented` (409); iptal edilmiş ücret `EDocuments.ChargeVoided` (409). Eşzamanlı iki istek aynı ücreti alamaz (ücret eşzamanlılık belirteci; kaybeden `EDocuments.ConcurrencyConflict`, 409).
- Bağlantı yoksa / yeniden yetki gerekiyorsa `EDocuments.NotConnected` (409).
- `note` (≤ 500) belgeye not olarak gider.
- Akış: belge kaydı (kimliği ve sağlayıcıya giden dış referans UUID'si bizde üretilir) **gönderimden önce** yazılır → sağlayıcıya gönderilir → yanıt `EDocumentDto`. e-SMM genelde hemen `Issued`; e-Arşiv / e-Fatura `Sending` döner, arka plan işi sonucu alır. Kalıcı hata `Failed` + `lastError`.
- Yanıt 200 `EDocumentDto` (§4.4). Denetim: `EDocument.Issue`.

### 4.3 Liste — `GET /e-documents`

Sorgu: `clinicId`, `clientId`, `status`, `documentType`, `fromUtc`, `toUtc`, `page` (1), `pageSize` (20, en çok 100). Yanıt standart sayfalı zarf `{ items: EDocumentListItemDto[], page, pageSize, totalItems, totalPages }`; sıralama oluşturma zamanı azalan. Liste öğesi: `id, clinicId, clientId, clientName, documentType, status, documentNumber, totalAmount, createdAtUtc, issuedAtUtc, isTest, lastErrorMessage`.

### 4.4 Detay — `GET /e-documents/{id}`

```json
{
  "id": "…", "clinicId": "…", "clientId": "…", "clientName": "…",
  "documentType": "EArchive", "status": "Issued",
  "provider": "parasut", "providerName": "Paraşüt", "isTest": true,
  "documentNumber": "GIB2026000000001", "documentUuid": "…",
  "buyer": { "kind": "Individual", "name": "…", "identityMasked": "100******46", "isUnknownBuyer": false, "taxOffice": null, "province": null, "district": null, "addressLine": null },
  "lines": [ { "chargeId": "…", "description": "Muayene", "quantity": 1, "grossAmount": 1200.00, "vatRate": 20, "lineKind": "Service" } ],
  "totalAmount": 1200.00, "providerTotal": 1200.00, "withholdingRate": null,
  "saleId": null, "note": null,
  "createdAtUtc": "…", "createdByUserId": "…", "issuedAtUtc": "…",
  "cancellableUntilUtc": "…", "canCancel": true, "canRetry": false, "canDiscard": false, "canDownloadPdf": true,
  "cancelReason": null, "cancelRequestedAtUtc": null, "cancelledAtUtc": null,
  "lastError": null, "attemptCount": 1
}
```

Detay ve liste öğesi ayrıca `origin`: `Manual` | `Payment` | `Automatic` taşır.

`lastError`: `{ "kind": "InvalidData|Authorization|AccountSuspended|QuotaExceeded|ProviderRejected|Transient|Unknown", "message": "Türkçe, kullanıcıya gösterilecek metin" }`. Sağlayıcı yanıt kodu denetim kaydına ve `lastError.providerCode`'a yazılır; belirteç/kişisel veri yazılmaz.

### 4.5 Eylemler

| Uç | İzin | Kural |
|---|---|---|
| `POST /e-documents/{id}/refresh` | Read | `Sending` / `CancelRequested` belgenin durumunu hemen sağlayıcıya sorar; diğer durumlarda mevcut durumu döner |
| `POST /e-documents/{id}/retry` | Issue | Yalnız `Failed`; aynı dış referansla yeniden gönderir (çift belge oluşmaz) |
| `POST /e-documents/{id}/discard` | Issue | Yalnız `Failed`; `Discarded`, ücretler serbest. Gövde `{ "reason" }` (zorunlu, ≤ 500) |
| `POST /e-documents/{id}/cancel` | Cancel | Yalnız `Issued` / `CancelRejected` ve iptal süresi içinde (`EDocuments.CancelWindowExpired`, 409). Gövde `{ "reason" }` zorunlu. Başarıda `Cancelled`, sağlayıcı reddederse `CancelRejected` |
| `GET /e-documents/{id}/pdf` | Read | Yalnız `Issued` / `CancelRequested` / `CancelRejected` / `Cancelled`; PDF sağlayıcıdan alınır, saklanmaz (`application/pdf`). Hazır değilse `EDocuments.PdfNotReady` (409) |

Geçersiz durum geçişi `EDocuments.InvalidState` (409). Hepsi 200 `EDocumentDto` döner (PDF hariç). Denetim: `EDocument.Retry`, `EDocument.Discard`, `EDocument.Cancel`.

### 4.6 Belgesiz ücretler — `GET /e-documents/undocumented-charges`

AK-4 "belgesi kesilmemiş ücretler" listesi. Sorgu: `clinicId`, `clientId`, `visitId`, `fromUtc`, `toUtc`, `page`, `pageSize`. İptal edilmemiş ve belgeye bağlı olmayan ücretler (`Discarded` / `Cancelled` belgeden serbest kalanlar dahil) **ve kesilemeyen (`Failed`) belgeye bağlı ücretler**. Öğe: `chargeId, clientId, clientName, petId, petName, visitId, saleId, description, amount, outstandingAmount, createdAtUtc, failedDocumentId, reason`. `reason` (kullanıcı metni): "Belge kesilemedi: …" (sağlayıcı hatası; `failedDocumentId` ile yeniden dene / vazgeç), "Müşterinin vergi numarası eksik.", "Ödeme bekliyor."; hazırsa null. İzin `EDocuments.Read`.

### 4.7 Ücret alanı

`ChargeDto` (liste, detay) `eDocumentId` (guid?, okunur) taşır: ücretin bağlı olduğu geçerli belge.

---

### 4.8 Ödemeden kes — `POST /e-documents/from-payment`

Gövde `{ "paymentId": "…" }`. `EDocuments.Issue`. Ödeme ekranındaki "e-Belge kes": ödemenin (geri alınmamış) tahsislerinin karşıladığı ücretlerden **tamamen ödenmiş ve belgesiz** olanlar belge türüne göre gruplanır, her grup ayrı belge olarak kesilir (`origin: Payment`). Kısmi ödenen ücret ödeme tamamlanınca kesilir. Tekrar koruması ödeme kimliğinden türetilen requestId ile (aynı ödeme iki kez gönderilse de çift belge yok). Ödeme kaydı belge hatasından etkilenmez; belge `Failed` olur ve yeniden denenebilir.

```json
{ "documents": [ EDocumentDto ], "skipped": [ { "chargeId": "…", "code": "EDocuments.BuyerIdentityMissing", "message": "…" } ] }
```

`documents` ödemenin ücretlerine bağlı mevcut belgeleri de içerir. Bağlantı yoksa `EDocuments.NotConnected` (409); ödeme yoksa `Payments.NotFound` (404).

### 4.9 Hatırlatma — `GET /e-documents/reminders?clinicId=`

`EDocuments.Read`. Menü rozeti: `{ "connected": true, "undocumentedPaidCount": 3, "failedCount": 1 }`. `undocumentedPaidCount`: bağlantı kurulduktan sonra yazılmış, tamamen ödenmiş ve belgesiz ücretler (bağlantıdan önceki geçmiş ücretler hatırlatmaya girmez); `failedCount`: kesilemeyen belgeler. Bağlantı yoksa `connected: false` ve sıfırlar.

### 4.10 Gün sonu bildirimi

Bağlı her klinikte `endOfDayTime`'da (ayar yoksa 23:00, otomatik kapalı olsa da) günde bir kez, kliniğe erişen ve `EDocuments.Issue` izni olan kullanıcılara bildirim (`type: EDocumentsDailySummary`, `targetType: "EDocuments"`, `targetId` = klinik): "N belge otomatik kesildi · M belge kesilemedi · K ödenmiş ücretin belgesi yok". Hepsi sıfırsa bildirim yok.

## 5) Sağlayıcı soyutlaması (uygulama notu)

- `IEDocumentProvider` (Application): mükellef sorgusu, belge gönder, durum sorgula, iptal, PDF, bağlantı sına; `IEDocumentOAuthProvider` OAuth bağlanma adımları. Paraşüt uygulaması `Infrastructure/EDocuments/Parasut`. Domain'de sağlayıcı adı geçmez; bağlantı `providerCode` metniyle sağlayıcıyı seçer. İkinci entegratör yeni bir `IEDocumentProvider` kaydıdır.
- Hata sınıfları adaptörde çevrilir: geçersiz veri, yetki (bağlantı `ReauthorizationRequired`), hesap askıda (Paraşüt 402), kontör, sağlayıcı reddi, geçici (otomatik yeniden deneme).
- Paraşüt akışı: alıcı cari (`contacts`, vergi no ile aranır, yoksa oluşturulur) → satır ürünü (`products`; Paraşüt satırda ürün/hizmet ister; satır açıklamasından türetilen sabit kodla `VTN-…` aranır, yoksa oluşturulur) → satış faturası (`sales_invoices`, `external_reference_key` = belgenin dış referansı; tekrarında mevcut fatura kullanılır) → e-SMM (`e_smms`, eşzamanlı) / e-Arşiv (`e_archives`) / e-Fatura (`e_invoices`, `scenario: basic`, `to` = gelen kutusu adresi) → iş takibi (`trackable_jobs`) → `active_e_document`. İptal `DELETE sales_invoices/{id}/cancel`. PDF `…/{id}/pdf` (adres süreli, saklanmaz).
- Ayarlar (`EDocuments:Parasut`): `ClientId`, `ClientSecret` (user-secrets / ortam değişkeni; repoda yok), `Environment` (`Test`/`Live`), `IdentityBaseUrl`, `ApiBaseUrl`, `RedirectUri`. `EDocuments:Processor`: `Enabled`, `IntervalSeconds`, `BatchSize`, `MaxAttempts`. Data Protection anahtar halkası: `DataProtection:KeysPath` (canlıda kalıcı ve yedekli dizin; boşsa varsayılan konum ve açılışta uyarı).

## 6) Otomatik kesme (arka plan işi)

- `EDocumentAutomationProcessor` (`EDocuments:Processor:AutomationIntervalSeconds`, varsayılan 60 sn) bağlı klinikleri tarar; her klinik kendi kiracı kapsamında işlenir.
- Uygun ücret: tamamen ödenmiş (`allocatedAmount = amount`), iptal edilmemiş, belgesiz, `autoIssueEnabledAtUtc`'den sonra yazılmış; bağlantı sağlam; alıcı belge türü için yeterli (kurumsalda vergi no). Uygun olmayan veya hata alan ücret kesilmez, §4.6 listesinde nedeniyle görünür ve §4.9–§4.10 hatırlatmasına girer.
- Gruplama müşteri başına, belge türüne göre; tekrar koruması anahtarla türetilen requestId + "bir ücret tek geçerli belge" bağı (otomatik ve elle kesme yarışsa da çift belge yok).
- Otomatik belgenin `createdByUserId`'si ayarı en son kaydeden kullanıcıdır, `origin: Automatic`. Her otomatik belge denetim kaydına `EDocument.AutoIssue` olarak yazılır (aktör yok, yük `origin: "Automatic"`).
- Gün sonu işaretlemesi işten önce kaydedilir (`ClinicEDocumentSettings.RowVersion`); iki örnek aynı günü ikinci kez çalıştırmaz.
- Deneme kiracısında canlı ortamda otomatik kesme yapılmaz.
- Migration: `AddEDocumentAutomation` (`ClinicEDocumentSettings`, `EDocuments.Origin`); yalnız yerel geliştirme veritabanında uygulandı.
