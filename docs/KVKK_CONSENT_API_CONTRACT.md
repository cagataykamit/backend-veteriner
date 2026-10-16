# KVKK açık rıza formu ve aydınlatma metni — API sözleşmesi (ADR-021, ADR-024, ADR-025, ADR-032)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-021-kvkk-consent-form.md`, `decisions/ADR-024-hukuki-metin-taslaklari.md`, `decisions/ADR-025-hasta-listesi-aktif-tedavi-kvkk-ret.md` (madde 4: Ret → hatırlatma kapanır). İlgili: `PET_STATUS_CLIENT_CONTACT_API_CONTRACT.md` (§2 `remindersOptedOut`), ADR-019 K3. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feat/kvkk-riza-formu`. Migration `20261014211943_AddClientKvkkConsentAndClinicKvkkFormTemplate` üretildi, **hiçbir paylaşılan/geliştirme veritabanına uygulanmadı**. **Yeni izin yok**, seed gerekmez (yalnız yeni composite okuma policy'si, aşağıda).

> **Aydınlatma şablonu (ADR-024):** dal `feat/aydinlatma-sablonu`. Migration `20261015160213_AddClinicKvkkDisclosureFormTemplate` üretildi (`ClinicKvkkFormTemplates.Kind` + benzersiz indeks `(TenantId, ClinicId, Kind)`; mevcut kayıtlar `Kind = 0` = açık rıza olur, metinleri değişmez), **hiçbir veritabanına uygulanmadı**. Yeni izin yok.

> **Rıza metni sürümü (ADR-032 madde 1):** dal `feat/kvkk-riza-surum`. Migration `20261016042924_AddKvkkConsentTemplateVersion` üretildi (`ClinicKvkkFormTemplates.TextVersion` int + `TextHash` char(64); `Clients.KvkkConsentTemplateVersion` int null + `KvkkConsentTemplateHash` char(64) null), **hiçbir paylaşılan/geliştirme veritabanına uygulanmadı**. Mevcut şablon satırları sürüm 1 ve metinlerinin özetiyle doldurulur (SQL Server 2019+ UTF-8 harmanlaması); mevcut müşteri rıza kayıtları **doldurulmaz** (null = sürüm bilinmiyor). Yeni izin yok.

> **Hukuki metinler taslaktır (ADR-024, ADR-021 madde 2'yi değiştirir):** Varsayılan açık rıza ve aydınlatma metinleri ürün ekibinin yazdığı, **hukuki onaydan geçmemiş** taslaklardır (ürün deposu `legal/acik-riza-metni.md`, `legal/aydinlatma-metni.md`). İlk satırları "Bu metin hukuk danışmanınızca onaylanmış … metniyle değiştirilmelidir." uyarısıdır; köşeli parantezli alanları (`[klinik adresi]`, `[başvuru kanalı …]` vb.) klinik doldurur. Varsayılan **yalnız klinikte o türde kayıt yokken** GET'te döner; kayıtlı (kliniğin yapıştırdığı) metin hiçbir zaman ezilmez, varsayılan veritabanına yazılmaz.

Akış: müşteri kartından "KVKK formu yazdır" → istemci şablonu (`GET /clinics/{clinicId}/kvkk-form-template`) alır, yer tutucuları doldurur, yazdırır → müşteri kâğıdı imzalar → personel "Rıza durumu"nu (`PUT /clients/{id}/kvkk-consent`) Kabul/Ret ve tarihle işaretler. Form kâğıtta kalır; sistem yalnız sonucu, tarihini ve işaretleyeni tutar.

---

## 1) Müşteri rıza durumu

### Alanlar (`GET /api/v1/clients/{id}` — `ClientDetailDto`'ya eklendi)

| Alan | Tip | Varsayılan | Anlam |
|---|---|---|---|
| `kvkkConsentStatus` | string: `None` \| `Accepted` \| `Declined` | `None` | İmzalı formun sonucu. `None` = form alınmadı (**mevcut tüm müşteriler**) |
| `kvkkConsentRecordedOn` | `yyyy-MM-dd` \| null | null | İmzalı formun tarihi (İstanbul takvim günü); yalnız `Accepted`/`Declined` iken dolu |
| `kvkkConsentRecordedByUserId` | guid \| null | null | Durumu işaretleyen kullanıcı |
| `kvkkConsentRecordedByName` | string \| null | null | İşaretleyenin görünen adı (bulunamazsa null) |
| `kvkkConsentTemplateVersion` | int \| null | null | İşaretleme anında geçerli açık rıza metninin sürümü (ADR-032): `0` = taslak varsayılan metin, `1+` = kliniğin kaydettiği metnin sürümü, `null` = **sürüm bilinmiyor** (ADR-032 öncesi kayıtlar, klinik bağlamı olmayan istek veya `None`) |
| `kvkkConsentTemplateHash` | string \| null | null | O metnin SHA-256 özeti (64 karakter, küçük harf hex; **tam** değer). `kvkkConsentTemplateVersion` ile birlikte dolu/boş |
| `rowVersion` | string (Base64) | — | Müşteri kaydının sürümü; `PUT …/kvkk-consent` için zorunlu |

Liste (`GET /clients`), hızlı kayıt ve müşteri oluşturma **değişmedi**; rıza yalnız aşağıdaki uçla değişir.

### `PUT /api/v1/clients/{id}/kvkk-consent`

İzin: mevcut **`Clients.Update`**. Audit: `Client.KvkkConsent.Accepted` / `.Declined` / `.None` (hedef `ClientId=…`).

```json
{ "status": "Accepted", "recordedOn": "2026-10-15", "rowVersion": "AAAAAAAAB9E=" }
```

- `status` zorunlu (`"None"` \| `"Accepted"` \| `"Declined"`; sayı `0`/`1`/`2` de kabul edilir).
- `recordedOn` opsiyonel; **verilmezse bugün** (İstanbul). Gelecek tarih reddedilir; geçmiş tarih serbest (eski kâğıt formlar).
- `status = "None"`: yanlış girişin geri alınması; `recordedOn` **verilemez**; tarih, işaretleyen ve metin sürümü/özeti temizlenir (geçmiş, audit kaydında kalır).
- **Metin sürümü (ADR-032):** `Accepted`/`Declined` işaretlenirken sunucu, isteğin **klinik bağlamındaki** (JWT `clinic_id`, yoksa `X-Clinic-Id` / `clinicId`) o anki açık rıza şablonunun sürümünü ve özetini kayda yazar; değerler `GET /clinics/{clinicId}/kvkk-form-template` yanıtındaki `textVersion` / `textHash` ile aynıdır (kayıt yoksa varsayılan metin: `0`). İstemci sürüm göndermez. Klinik bağlamı yoksa veya klinik kiracıda bulunamazsa ikisi de `null` yazılır (işaretleme yine başarılı). Tekrar işaretleme (tarih düzeltme dahil) o anki sürümü yeniden yazar.
- Zaten `Accepted` iken tekrar `Accepted` göndermek tarihi/işaretleyeni düzeltir; `Accepted` ↔ `Declined` doğrudan geçilebilir.
- `rowVersion` zorunlu: `GET /clients/{id}` yanıtındaki değer. Arada kayıt değiştiyse 409.
- **Ret hatırlatmayı kapatır (ADR-025 madde 4, ADR-021 madde 4'ü değiştirir):** `Declined` işaretlemek `remindersOptedOut = true` yapar (zaten açıksa açık kalır; tarih düzeltmek için tekrar `Declined` göndermek de bayrağı açar). `Accepted` veya `None`'a geçmek bayrağı **kapatmaz**; bayrak yalnız müşteri formundaki "Hatırlatma istemiyor" ile elle kapanır (güvenli taraf). Denetim izi: `Client.KvkkConsent.Declined` audit kaydı bayrak değişikliğini de kapsar (ayrı audit satırı yazılmaz).

Yanıt **200** (`ClientKvkkConsentDto`, alan adları detay yanıtıyla aynı). `remindersOptedOut` yazımdan sonraki bayraktır (ADR-025; istemci müşteri kartındaki "Hatırlatma istemiyor" görünümünü bununla günceller):

```json
{
  "id": "guid",
  "kvkkConsentStatus": "Accepted",
  "kvkkConsentRecordedOn": "2026-10-15",
  "kvkkConsentRecordedByUserId": "guid",
  "kvkkConsentRecordedByName": "Ayşe Yılmaz",
  "kvkkConsentTemplateVersion": 2,
  "kvkkConsentTemplateHash": "3f1c…(64 karakter hex)",
  "remindersOptedOut": false,
  "rowVersion": "AAAAAAAAB9I="
}
```

| Kod | HTTP | Durum |
|---|---|---|
| `Clients.Validation` | 400 | `rowVersion` yok/bozuk; tarih gelecekte; `None` ile tarih verildi; geçersiz `status` |
| (model doğrulama) | 400 | Tanınmayan `status` metni (ör. `"Maybe"`) |
| `Clients.WalkInReadOnly` | 400 | Peşin müşteri (sistem kaydı) |
| `Clients.NotFound` | 404 | Müşteri yok / başka kiracı |
| `Clients.ConcurrencyConflict` | 409 | Kayıt başka biri tarafından değiştirildi; `GET /clients/{id}` ile yenile |
| `Tenants.TenantInactive` | 403 | Pasif kiracı |
| — | 403 | `Clients.Update` yok |

### Mevcut tam güncelleme (`PUT /api/v1/clients/{id}`)

- Rıza alanlarını **taşımaz ve değiştirmez**; `rowVersion` istemez (geriye uyumlu).
- `Clients` tablosuna `rowversion` eklendiği için, okuma ile yazma arasında kayıt değişirse artık 500 yerine **409 `Clients.ConcurrencyConflict`** döner.

---

## 2) Klinik form şablonu

Klinik başına tek kayıt (mevcut `appointment-settings` deseni: kayıt yoksa varsayılan döner). Sunucu **ham metni** saklar ve döner; yalnız boş/uzunluk denetimi yapar. **Yer tutucuları istemci doldurur** (yazdırma görünümü).

### `GET /api/v1/clinics/{clinicId}/kvkk-form-template`

İzin: composite **any-of** policy `Clinics_KvkkFormRead` = `Clinics.Read` \| `Clinics.Update` \| `Clients.Read` (formu müşteri kartından yazdıran ve Ayarlar'da metni düzenleyen aynı metni okur). Klinik atama kapsamı uygulanır (atanmamış klinik → 403 `Clinics.AccessDenied`).

```json
{
  "clinicId": "guid",
  "text": "…",
  "isDefault": true,
  "rowVersion": null,
  "updatedAtUtc": null,
  "maxLength": 20000,
  "placeholders": ["{{clinicName}}", "{{clientName}}", "{{date}}"],
  "textVersion": 0,
  "textHash": "…(64 karakter hex)"
}
```

- `textVersion` / `textHash` (ADR-032): metnin sürümü ve ham metnin (yer tutucular doldurulmadan) SHA-256 özeti. Varsayılan metin `0`; kliniğin ilk kaydı `1`, kırpılmış metin her değiştiğinde `+1`; **aynı metin** tekrar kaydedilirse sürüm ve özet değişmez (`rowVersion` yine yenilenir). Eski bir metne dönmek de yeni sürümdür. Aydınlatma şablonu (§2b) aynı alanları aynı kuralla taşır; rıza kaydına yalnız açık rıza şablonunun sürümü yazılır.
- `isDefault = true`: kayıt yok; `text` taslak varsayılan açık rıza metnidir (ilk satır “Bu metin hukuk danışmanınızca onaylanmış açık rıza metniyle değiştirilmelidir.”, ardından “{{clinicName}} — KİŞİSEL VERİLERİN İŞLENMESİNE İLİŞKİN AÇIK RIZA BEYANI” gövdesi: hatırlatma/iletişim izni, kapsam dışı, serbestlik ve geri alma, haklar), `rowVersion = null`.
- `isDefault = false`: kliniğin kaydettiği metin; `rowVersion` dolu; `updatedAtUtc` son kayıt zamanı.
- `placeholders` ve `maxLength` sabittir (istemci editörde gösterebilir/sayaç için kullanabilir).

### Yer tutucular (istemcide doldurulur)

| Yer tutucu | Değer |
|---|---|
| `{{clinicName}}` | Aktif klinik adı |
| `{{clientName}}` | Müşteri adı soyadı (`fullName`) |
| `{{date}}` | Form tarihi (yazdırma günü, `dd.MM.yyyy` önerilir) |

- Tam metin eşleşmesi, büyük/küçük harf duyarlı; tanınmayan `{{…}}` olduğu gibi bırakılır. Sunucu yer tutucu doğrulaması **yapmaz**.
- Varsayılan açık rıza metninde yalnız `{{clinicName}}` geçer; `{{clientName}}` / `{{date}}` sabit imza bloğunda istemci tarafından basılır.
- Metin düz metindir (HTML değil); istemci satır sonlarını korumalı (`white-space: pre-wrap`) ve HTML olarak yorumlamamalıdır.
- "Kabul ediyorum / Kabul etmiyorum" kutuları ve "Veri Sahibi: Adı Soyadı, Tarih, İmza" bloğu **şablonun parçası değildir**; yazdırma görünümünün sabit düzenidir (metin ne olursa olsun basılır).

### `PUT /api/v1/clinics/{clinicId}/kvkk-form-template`

İzin: **`Clinics.Update`** (randevu varsayılanlarıyla aynı). Audit: `Clinic.KvkkFormTemplate.Update`.

```json
{ "text": "…", "rowVersion": "AAAAAAAAB9E=" }
```

- `text` zorunlu; baş/son boşluk kırpılır; boş olamaz; en fazla **20 000** karakter.
- `rowVersion`: GET `isDefault = true` iken **null/boş** gönderilir (ilk kayıt oluşur); `isDefault = false` iken GET'teki değer **zorunlu**.
  - Kayıt varken sürümsüz gönderim (iki kişi aynı anda varsayılandan kaydetti) → 409.
  - Kayıt yokken sürümlü gönderim → 409.
- Yanıt **200**: GET ile aynı şekil (`isDefault = false`, yeni `rowVersion`).
- "Varsayılana dön" ucu yok (bkz. açık sorular); istemci varsayılan metni bilmek isterse kayıt yokken GET'ten alır.

| Kod | HTTP | Durum |
|---|---|---|
| `Clinics.KvkkFormTemplate.Validation` | 400 | Metin boş / çok uzun; `rowVersion` bozuk |
| `Clinics.KvkkFormTemplate.Validation.InvalidRequestBody` | 400 | Gövde yok/bozuk JSON |
| `Clinics.NotFound` | 404 | Klinik yok / başka kiracı |
| `Clinics.AccessDenied` | 403 | Kliniğe atanmamış kullanıcı |
| — | 403 | `Clinics.Update` yok (policy) |
| `Clinics.KvkkFormTemplate.ConcurrencyConflict` | 409 | Sürüm uyuşmazlığı veya eşzamanlı ilk kayıt; GET ile yenile |

---

## 2b) Klinik aydınlatma metni şablonu (ADR-024)

Açık rıza formu şablonuyla (§2) **birebir aynı sözleşme**: aynı yanıt şekli (`ClinicKvkkFormTemplateDto`), aynı istek gövdesi, aynı yer tutucular, aynı 20 000 karakter sınırı, aynı RowVersion kuralı, aynı hata kodları (`Clinics.KvkkFormTemplate.*`) ve izinler. Yalnız yol ve varsayılan metin farklıdır. İki şablon **birbirinden bağımsızdır**: biri kaydedilince diğeri değişmez; her biri ayrı `rowVersion` taşır.

| Uç | İzin | Audit |
|---|---|---|
| `GET /api/v1/clinics/{clinicId}/kvkk-disclosure-template` | composite `Clinics_KvkkFormRead` (`Clinics.Read` \| `Clinics.Update` \| `Clients.Read`); klinik atama kapsamı | — |
| `PUT /api/v1/clinics/{clinicId}/kvkk-disclosure-template` | `Clinics.Update` | `Clinic.KvkkDisclosureTemplate.Update` |

```json
{
  "clinicId": "guid",
  "text": "Bu metin hukuk danışmanınızca onaylanmış aydınlatma metniyle değiştirilmelidir.\n\n{{clinicName}} — KİŞİSEL VERİLERİN İŞLENMESİNE İLİŞKİN AYDINLATMA METNİ\n\n…\n\nDüzenleme tarihi: {{date}}\nBilgilendirilen: {{clientName}}",
  "isDefault": true,
  "rowVersion": null,
  "updatedAtUtc": null,
  "maxLength": 20000,
  "placeholders": ["{{clinicName}}", "{{clientName}}", "{{date}}"]
}
```

- Varsayılan gövde: veri sorumlusu, işlenen veriler (madde içi satırlar `- ` ile başlar), amaçlar, hukuki sebepler (Kanun 5/2), toplanma yöntemi, aktarım, saklama, haklar (m.11), başvuru yolu. Düz metin; markdown/HTML yok.
- Aydınlatma bir **rıza beyanı değildir**: yazdırma görünümünde "Kabul ediyorum / Kabul etmiyorum" kutuları ve imza bloğu **basılmaz**. Varsayılan metin kendi sonunda `Düzenleme tarihi: {{date}}` ve `Bilgilendirilen: {{clientName}}` satırlarını taşır; istemci yalnız yer tutucuları doldurur.
- PUT gövdesi ve kuralları §2 ile aynı: `{ "text": "…", "rowVersion": null | "<GET'teki değer>" }`.

---

## 3) Kararlar

| # | Konu | Karar | Gerekçe |
|---|---|---|---|
| K1 | Rıza değerleri | `None` / `Accepted` / `Declined`; mevcut müşteriler `None`. | ADR-021 madde 3; nullable yerine `None` ile "alınmadı" açıkça okunur (iletişim kanalı deseni). |
| K2 | Ayrı uç | Rıza yalnız `PUT /clients/{id}/kvkk-consent` ile değişir; tam PUT taşımaz. RowVersion zorunlu. | Bilinçli, tek amaçlı eylem (imzalı kâğıt alındı); alanı bilmeyen eski istemci rızayı silemez; eşzamanlı işaretlemede kayıp güncelleme olmaz (`PUT /pets/{id}/status` deseni). |
| K3 | İzin | Mevcut `Clients.Update`; yeni izin yok. | Müşteri kaydını düzenleyen (resepsiyon dahil) formu imzalatır; ADR-021 "mevcut müşteri güncelleme izni". |
| K4 | Tarih | Opsiyonel; verilmezse bugün; gelecek yok; geçmiş serbest. | Form çoğunlukla aynı gün işaretlenir; eski kâğıt formlar geriye tarihli girilebilmeli. |
| K5 | Geri alma | `None` tarih ve işaretleyeni temizler; geçmiş audit kaydında. | Yanlış girişin düzeltilmesi; ayrı rıza geçmişi modeli kapsam dışı (ADR-021 madde 5). |
| K6 | İşaretleyen | `kvkkConsentRecordedByUserId` müşteri satırında + audit (`IAuditableRequest`). | ADR-021 "kim işaretledi"; ayrı tablo gerekmedi. |
| K7 | Hatırlatma | **Ret → `remindersOptedOut = true`** (kural `Client.RecordKvkkConsent`'te); Kabul/Yok bayrağa dokunmaz, bayrak yalnız elle kapanır. Hatırlatma adayları mevcut `Client.AcceptsReminders` kuralıyla dışlanır. | ADR-025 madde 4 (ADR-021 madde 4'ü değiştirir); yanlışlıkla hatırlatma gitmesin diye güvenli taraf. Hukuki görüş (HUKUKI-ONAY-LISTESI A4) gelince gözden geçirilir. |
| K8 | Şablon kapsamı | **Klinik başına** (kiracı değil); ayrı tablo `ClinicKvkkFormTemplates`; kayıt yoksa varsayılan. | ADR-021 "klinik başına"; başlıkta klinik adı var; `ClinicAppointmentSettings` deseni. |
| K9 | Yer tutucu doldurma | İstemcide; sunucu ham metin, yalnız boş/uzunluk denetimi. | Yazdırma görünümü istemcide; sunucu tarafı biçimlendirme/tarih yerelleştirmesi gerekmez. |
| K10 | Şablon okuma izni | Composite `Clinics_KvkkFormRead` (`Clinics.Read` \| `Clinics.Update` \| `Clients.Read`). | Formu yazdıran resepsiyonda `Clinics.Read` olmayabilir; metin gizli veri değildir. Yeni izin değil, policy. |
| K11 | Şablon eşzamanlılığı | RowVersion; kayıt yokken sürümsüz oluşturma, varken zorunlu; benzersiz indeks yarışını 409'a çevirir. | İki yönetici aynı anda düzenlerse sessiz üzerine yazma olmasın. |
| K12 | Uzunluk | En fazla 20 000 karakter, düz metin. | Tipik açık rıza/aydınlatma metinleri birkaç bin karakter; HTML/biçim yok (XSS ve yazdırma tutarlılığı). |
| K13 | Aydınlatma şablonu saklama | Aynı tablo `ClinicKvkkFormTemplates`, `Kind` sütunu (`0` Consent, `1` Disclosure); benzersiz `(TenantId, ClinicId, Kind)`. Aynı command/query, türü yol belirler. | ADR-024 "ADR-021 ile aynı desen"; boş/uzunluk kuralı tek yerde (`ClinicKvkkFormTemplate`) kalır, ikinci tablo ve kopya kod gerekmez. |
| K14 | Varsayılan metinler | ADR-024 taslakları (`legal/`), uyarı satırıyla; yalnız kayıt yokken döner, veritabanına yazılmaz. | Kliniğin yapıştırdığı metin ezilmez; taslak güncellenince kaydı olmayan klinikler yeni taslağı otomatik görür. |
| K15 | Açık rıza metnindeki kutu/imza satırları | Taslaktaki "☐ Kabul ediyorum ☐ Kabul etmiyorum" ve "Veri Sahibi … İmza" satırları varsayılan metne **konmadı**. | Bunlar yazdırma görünümünün sabit düzenidir (§2); metne konsaydı iki kez basılırdı. |
| K16 | Şablon metin sürümü | `ClinicKvkkFormTemplate.TextVersion`: ilk kayıt 1, kırpılmış metin değişince +1, aynı metin artırmaz; varsayılan metin 0 (`DefaultTextVersion`). Kural yalnız `ClinicKvkkFormTemplate.ApplyText`'te. Klinik + tür başına bağımsız sayaç. | ADR-032 madde 1; "aynı metin" yeni sürüm sayılsaydı içeriği aynı iki sürüm ispatı bulandırırdı. |
| K17 | Metin özeti | SHA-256, metnin UTF-8 baytları, küçük harf hex, tam 64 karakter (`ClinicKvkkFormTemplate.ComputeTextHash`); ham şablon metni (yer tutucular doldurulmadan). Metnin kendisi rıza kaydında saklanmaz. | ADR-032 "metnin kendisi tekrar saklanmaz"; varsayılan taslak sonradan güncellenirse sürüm 0 aynı kalır, hangi taslak olduğu özetle ayırt edilir. |
| K18 | Rıza kaydına yazım | `Accepted`/`Declined` işaretlenirken isteğin klinik bağlamındaki açık rıza şablonunun sürüm+özeti `Client` satırına yazılır (`Client.RecordKvkkConsent`); istemci göndermez. `None` temizler. Klinik bağlamı yoksa null. | Müşteri kiracı düzeyinde, şablon klinik düzeyinde; formu yazdıran aktif klinik ile işaretleyen aynı oturumdur. Sunucu belirler ki istemci uydurmasın. |
| K19 | Eski kayıtlar | Müşteri rıza kayıtları geriye dönük doldurulmaz (null = bilinmiyor); mevcut şablon satırları migration'da sürüm 1 + özetle doldurulur. | ADR-032 "geriye dönük uydurulmaz" rıza kaydı içindir; şablonun kendi metni zaten saklıdır, özeti ondan türetilir. |

---

## 4) Frontend için değişenler

1. **Müşteri kartı – Rıza durumu:** `kvkkConsentStatus` rozeti (`None` = "KVKK formu alınmadı"), tarih ve işaretleyen adı. "İşaretle" diyaloğu: Kabul / Ret, tarih (varsayılan bugün, ileri tarih seçilemez), "Yok'a geri al". `PUT /clients/{id}/kvkk-consent`, `rowVersion` `GET /clients/{id}`'den; 409 → detayı yenile; yanıttaki `rowVersion` sonraki yazımda kullanılır. Yanıttaki `remindersOptedOut` ile "Hatırlatma istemiyor" görünümü güncellenir; Ret sonrası kısa bildirim önerilir ("Ret işaretlendi; hatırlatmalar kapatıldı.").
2. **Müşteri kartı – KVKK formu yazdır:** `GET /clinics/{aktifKlinikId}/kvkk-form-template` → yer tutucuları doldur → `@media print` görünümü (başlıkta klinik adı, metin, Kabul ediyorum / Kabul etmiyorum kutuları, Veri Sahibi Adı Soyadı/Tarih/İmza bloğu). `isDefault = true` ise ekranda "Metin henüz düzenlenmedi" uyarısı önerilir.
3. **Ayarlar – KVKK form metni:** GET ile yükle, `placeholders` listesini ve `maxLength` sayacını göster, PUT ile kaydet (`rowVersion` `isDefault` iken null). 409 → yeniden yükle.
4. Yeni hata kodları: `Clients.ConcurrencyConflict` (409; ayrıca tam müşteri PUT'unda da dönebilir), `Clinics.KvkkFormTemplate.Validation` (400), `Clinics.KvkkFormTemplate.ConcurrencyConflict` (409).
5. **Ayarlar – Aydınlatma metni (ADR-024):** KVKK form metni ekranıyla aynı bileşen, yol `/clinics/{id}/kvkk-disclosure-template`. `isDefault = true` iken "Taslak metin; hukuk danışmanınızca onaylanmadan kullanmayın, köşeli parantezli alanları doldurun" uyarısı önerilir (açık rıza için de aynı).
6. **Müşteri kartı – Aydınlatma metni yazdır:** GET → yer tutucuları doldur → `@media print`; kutu/imza bloğu yok (§2b).
7. **Müşteri kartı – Metin sürümü (ADR-032):** rıza durumunun yanında "Metin sürümü: N" (`kvkkConsentTemplateVersion`); `0` → "Varsayılan metin", `null` (Kabul/Ret iken) → "Bilinmiyor"; `None` iken gösterilmez. `PUT …/kvkk-consent` yanıtındaki değerle güncellenir. Açık rıza formu yazdırmada şablonun `textVersion`'ı aynı biçimde basılabilir (işaretleme anında geçerli sürüm, yazdırılan sürümle aynı olmalıdır; metin arada değiştiyse yeni form yazdırılmalı).

---

## 5) Bilinen eksikler ve açık sorular

- **Hukuki metinler ve saklama süresi doğrulanmadı;** varsayılan açık rıza ve aydınlatma metinleri taslaktır, pilot canlı verisinden önce KVKK danışmanı onaylamalı (ADR-021 riskler, ADR-024 madde 5).
- Rıza kaydı yalnız **işaretleme anındaki** sürümü tutar (ADR-032); müşteriye yazdırılan kâğıdın sürümüyle eşleştiğini sunucu doğrulayamaz (form yazdırıldıktan sonra metin değiştiyse kayıt yeni sürümü gösterir). Eski sürümlerin metinleri saklanmaz; ispat için klinik eski metni kendi arşivinde tutmalı, sistem yalnız özetle eşleşmeyi doğrular.
- Klinik bağlamı olmayan istekte (token'da `clinic_id` yok, header/query yok) sürüm `null` yazılır.
- Ret → hatırlatma bağı tek yönlüdür (K7, ADR-025); hukuki görüş (A4) gelince gözden geçirilir.
- Rıza geçmişi (sürümleme), tarama/dosya ekleme, e-imza, SMS/e-posta onayı, İYS: kapsam dışı (ADR-021 madde 5). Geri almada önceki değer yalnız audit kaydında.
- Şablon için "varsayılana dön" (DELETE) ucu yok; gerekirse eklenir.
- Müşteri listesinde rıza durumu/süzgeci yok ("formu alınmamış müşteriler" listesi istenirse ayrı iş).
- İçe aktarma (ADR-019) rıza durumu taşımaz; içe aktarılan müşteriler `None` başlar.
- Query DB okuma modeline rıza alanları eklenmedi (detay komut DB'den okunur).
