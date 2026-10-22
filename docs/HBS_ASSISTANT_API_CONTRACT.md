# HBS yardımcısı v1 API sözleşmesi (ADR-036 Dilim 2)

Backend tek doğruluk kaynağıdır. Kaynak karar: ADR-036 (vetinity-product, kabul edildi) — Dilim 2, AK-5(a) **manuel yardımcı**, AK-8 **Vetinity HBS kimlik bilgisi saklamaz** (hekim HBS'e e-Devlet ile kendisi girer). İlgili: `INVOICING_IDENTITY_API_CONTRACT.md` (TCKN/VKN, `Pet.hbsAnimalId`), `PRESCRIPTION_ITEMS_API_CONTRACT.md`, `ADMINISTRATIONS_API_CONTRACT.md`, `REGISTERS_API_CONTRACT.md`, `OPEN_ITEMS_API_CONTRACT.md`. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme + uygulama (dal `feat/hbs-yardimcisi-d2`). Migration yazıldı, veritabanına uygulanmadı.
> **Mevzuat doğrulanmadı:** hangi reçetenin e-Reçete'ye, hangi aşının ATS'e girilmesi gerektiği kodda **sabit kural değildir**. Her reçete ve Vetinity'de uygulanan her aşı "HBS takibi"ne girer; gerekmeyen kaydı hekim gerekçeyle **Gerekmiyor** işaretler. Arayüz "zorunludur" demez; "HBS'e girilmedi" nötr ifadedir.
> **Kapsam dışı:** HBS'e otomatik gönderim (v2, resmî web servis izni D9), HBS kimlik bilgisi, ev hayvanı kimliklendirme (çip/pasaport) kaydı, muayene defterindeki "e-Reçete Kimlik" sütununun doldurulması (ayrı küçük iş).

---

## 1) Kavramlar ve kurallar (tek yerde)

| Kavram | Kural | Yer |
|---|---|---|
| HBS durumu | `NotEntered` (Girilmedi, varsayılan) · `Entered` (Girildi) · `NotRequired` (Gerekmiyor) | `Domain/Hbs/HbsEntryStatus` |
| Geri kayıt doğrulaması | `Entered`: numara **isteğe bağlı**, kırpılır, en çok 50 karakter; gerekçe yok sayılır. `NotRequired`: gerekçe **zorunlu** 5–500 karakter (`CorrectionReason`); numara yok sayılır. `NotEntered`: numara, gerekçe, işaretleyen ve zaman temizlenir. İşaretleyen kullanıcı ve zaman (`markedByUserId`, `markedAtUtc`) `Entered`/`NotRequired` yazılırken atanır | `Domain/Hbs/HbsEntryRules` |
| Reçete numarası | e-Reçete No (HBS'ten dönen) | `Prescription.Hbs*` |
| Aşı numarası | ATS aşı uygulama belgesi seri no; **aşı kaydında** (`Vaccination`) tutulur. Gerekçe: ATS bir aşı olayını kaydeder; aşı kaydı ürünlü (stoklu, bağlı `Administration`) da ürünsüz de olabilir, uygulama yalnız stok düşümüdür | `Vaccination.Hbs*` |
| Aşıda geri kayıt koşulu | Yalnız `status = Applied` aşı kaydı işaretlenir; diğerinde `Vaccinations.HbsNotApplicable` (400) | `Vaccination.ApplyHbsEntry` |
| HBS takibi kapsamı | Reçete: tüm reçeteler. Aşı: `status = Applied` **ve** içe aktarılmamış (`importBatchId = null`; eski sistemden gelen geçmiş aşılar takibe girmez) | `Application/Hbs/HbsTracking` (EF ifadeleri) |
| Kilit (ADR-013) | Kesinleşmiş muayeneye bağlı reçete/aşının **klinik içeriği** kilitlidir; HBS geri kaydı klinik içerik değil, **ek bilgi**dir. HBS PATCH ucu `ExaminationLinkedRecordLock`'u **çağırmaz**, mevcut PUT uçları HBS alanlarına dokunmaz. Kilit kuralları değişmedi | `ExaminationLinkedRecordLock` belge yorumu |

Yeni izin **yoktur**: okuma `Prescriptions.Read` / `Vaccinations.Read`, geri kayıt `Prescriptions.Update` / `Vaccinations.Update`. Seed değişikliği gerekmez.

---

## 2) Reçete kalemi yapısı (ADR-036 "serbest metin yetmez")

`PrescriptionItem`'a üç isteğe bağlı alan eklendi (ASAMA5 D4 "birim/lot yok" kararının yerini ADR-036 alır). İstek (`items[]`) ve yanıt (`GET /prescriptions/{id}` → `items[]`) aynı adlarla:

| Alan | Tip | Kural |
|---|---|---|
| `dose` | string? (≤100) | Uygulama başına doz, ör. "1 tablet", "0,5 ml" |
| `unit` | string? (≤30) | `quantity` birimi, ör. "kutu", "şişe". Boşsa ve `productId` verilmişse **ürünün stok birimi** kopyalanır (ad kopyalamasıyla aynı yer: `PrescriptionItemResolver`) |
| `lotNumber` | string? (≤100) | Parti/seri no (serbest metin; reçete stok düşmez, parti kaydına bağlanmaz) |

Diğer satır kuralları (`PRESCRIPTION_ITEMS_API_CONTRACT.md`) değişmedi; kalem yazma yine kesinleşmiş muayene kilidine tabidir. Eski istemci alanları göndermezse `null` yazılır (satırlar tam değiştirme ile yazıldığından eski PUT zaten tüm satırı yeniden yazar).

---

## 3) Okuma: HBS alanı detay ve listelerde

### Detay
`GET /prescriptions/{id}` ve `GET /vaccinations/{id}` yanıtına `hbs` nesnesi (`HbsEntryDto`):

```json
"hbs": {
  "status": "Entered",
  "referenceNumber": "2RX4K9",
  "notRequiredReason": null,
  "markedAtUtc": "2026-10-22T09:15:00Z",
  "markedByUserId": "guid",
  "markedByName": "Dr. Ayşe Yılmaz",
  "isTracked": true
}
```

`isTracked`: kayıt HBS takibi kapsamında mı (§1). Takip dışı aşıda (planlı/iptal/içe aktarılmış) `false`; arayüz rozet göstermez.

### Liste süzgeci ve sütunu
- `GET /prescriptions?hbsStatus=NotEntered|Entered|NotRequired` ve `GET /vaccinations?hbsStatus=…`: süzgeç takip kapsamıyla birlikte uygulanır (aşıda `Applied` + içe aktarılmamış). Verilmezse süzgeç yok.
- Liste satırına `hbsStatus` eklenir (`PrescriptionListItemDto`, `VaccinationListItemDto`): reçetede her zaman dolu; aşıda takip dışıysa `null`.

---

## 4) `GET /prescriptions/{id}/hbs-data` ve `GET /vaccinations/{id}/hbs-data`

HBS ekranında girilecek alanların hazır dizisi. İzin `Prescriptions.Read` / `Vaccinations.Read`. Kiracı + klinik kapsamı detay ucuyla aynıdır (başka kiracı/atanmamış klinik → `…NotFound` 404). Her çağrı denetim kaydına yazılır (`Prescription.HbsData.Read` / `Vaccination.HbsData.Read`, hedef `PrescriptionId=…` / `VaccinationId=…`).

Yanıt `HbsDataDto`:

```json
{
  "recordKind": "Prescription",
  "recordId": "guid",
  "hbs": { "...": "HbsEntryDto (§3)" },
  "ownerIdentityMasked": false,
  "sections": [
    { "key": "animal", "title": "Hasta", "fields": [
      { "key": "animal.name", "label": "Adı", "value": "Pamuk" },
      { "key": "animal.species", "label": "Türü", "value": "Kedi" },
      { "key": "animal.breed", "label": "Irkı", "value": "Tekir" },
      { "key": "animal.microchip", "label": "Mikroçip No", "value": "900123456789012" },
      { "key": "animal.hbsAnimalId", "label": "HBS Hayvan Kimlik No", "value": null }
    ]},
    { "key": "owner", "title": "Hasta Sahibi", "fields": [
      { "key": "owner.name", "label": "Adı Soyadı", "value": "Ayşe Yılmaz" },
      { "key": "owner.identityNumber", "label": "T.C. Kimlik No", "value": "10000000146" },
      { "key": "owner.phone", "label": "Telefon", "value": "905321110001" }
    ]},
    { "key": "record", "title": "Reçete", "fields": [
      { "key": "record.date", "label": "Reçete Tarihi", "value": "22.10.2026" },
      { "key": "record.veterinarian", "label": "Veteriner Hekim", "value": "Dr. Ayşe Yılmaz" }
    ]},
    { "key": "item1", "title": "İlaç 1", "fields": [
      { "key": "item1.name", "label": "İlaç Adı", "value": "Amoksisilin 250 mg" },
      { "key": "item1.quantity", "label": "Miktar", "value": "2" },
      { "key": "item1.unit", "label": "Birim", "value": "kutu" },
      { "key": "item1.dose", "label": "Doz", "value": "1 tablet" },
      { "key": "item1.instructions", "label": "Kullanım", "value": "Günde 2 kez, 7 gün" },
      { "key": "item1.lotNumber", "label": "Parti/Seri No", "value": null }
    ]}
  ],
  "copyAllText": "Hasta\nAdı: Pamuk\n…"
}
```

- `value` HBS'e **kopyalanacak metindir** (sunucu biçimler): tarih `dd.MM.yyyy` (Europe/Istanbul; SKT takvim günü), miktar Türkçe ondalık (`0,5`). Boş alan `null` döner ama satır yine listelenir (hekim eksik alanı görür).
- `copyAllText`: dolu alanlar; her bölüm başlığı satırı ve altında `Etiket: değer` satırları, bölümler arasında boş satır. Bkz. `HbsDataBuilder`.
- **Hasta:** ad, tür, ırk (katalog ırkı, yoksa serbest metin), mikroçip, HBS hayvan kimlik no.
- **Hasta Sahibi:** ad soyad, kimlik no, telefon (`Client.Phone` saklandığı gibi). Kimlik no kişi türüne göre: `Individual` → TCKN (etiket "T.C. Kimlik No"), `Corporate` → vergi no (etiket "Vergi No", VKN veya şahıs şirketi TCKN'si); kişi türü yoksa değer `null`. **Maskesiz yalnız bu uçta ve yalnız `Clients.Update` izni varsa**; yoksa `TurkishTaxIdentity.Mask` ile maskeli ve `ownerIdentityMasked = true`. Denetim kaydında numara yoktur (istek yalnız kimlik içerir).
- **Reçete** (`record`): reçete tarihi, veteriner hekim. **İlaç n** (`item{n}`, satır sırasıyla): ilaç adı, miktar, birim, doz, kullanım talimatı, parti/seri no. Satırsız (eski, serbest metin) reçetede tek bölüm `item1` yalnız `item1.content` ("Reçete İçeriği" = `Content`).
- **Aşı** (`vaccine`): aşı adı, parti/seri no ve SKT (bağlı iptal edilmemiş uygulamanın partisinden; ürünsüz aşıda `null`), doz (`miktar birim`, bağlı uygulamadan), uygulama tarihi, veteriner hekim.
- **Veteriner hekim kuralı** (defterlerle aynı, `REGISTERS_API_CONTRACT.md` bölüm 4): kaydın bağlı olduğu muayenenin geliş (Visit) kaydındaki sorumlu hekim; yoksa `null`. Reçetede/aşıda ayrı hekim alanı yoktur.

Hatalar: `Tenants.ContextMissing` (400), `Prescriptions.NotFound` / `Vaccinations.NotFound` (404), yetkisiz 403, token yok 401.

---

## 5) Geri kayıt: `PATCH /prescriptions/{id}/hbs` ve `PATCH /vaccinations/{id}/hbs`

İzin `Prescriptions.Update` / `Vaccinations.Update`. Gövde:

```json
{ "status": "Entered", "referenceNumber": "2RX4K9", "notRequiredReason": null }
```

Yanıt 200 + güncel `HbsEntryDto` (§3).

- Kural §1 (`HbsEntryRules`). Ayrı küçük uç: **reçete/aşı PUT'u çağrılmaz**, klinik içerik ve kalemler değişmez; `updatedAtUtc` değişmez (HBS işareti klinik güncelleme sayılmaz).
- **Kesinleşmiş muayeneye bağlı kayıtta da çalışır** (ADR-013 kilidi klinik içeriği korur; bkz. §1).
- Kiracı pasifse `Tenants.TenantInactive` (403). Klinik ataması: kaydın kliniğine yazma kapsamı (`Clinics.AccessDenied` 403); JWT klinik bağlamı farklı klinikteki kayıt → `…NotFound` (404).
- Hatalar: `Prescriptions.HbsValidation` / `Vaccinations.HbsValidation` (400; numara > 50, gerekçe 5–500 değil, geçersiz durum), `Vaccinations.HbsNotApplicable` (400; `Applied` olmayan aşı), `…NotFound` (404).
- Denetim: `Prescription.Hbs.Update` / `Vaccination.Hbs.Update`, hedef `PrescriptionId=…` / `VaccinationId=…`; istek kaydı durum, numara ve gerekçeyi taşır (kim hangi numarayı girdi).
- Eşzamanlılık: son yazan kazanır (reçete/aşıda satır sürümü yoktur); işaretleyen ve zaman her yazımda güncellenir.

---

## 6) Takip listesi ve dışa aktarma (Rapor Merkezi)

Defter altyapısı (`REGISTERS_API_CONTRACT.md` bölüm 1: `from`/`to` İstanbul günü zorunlu ≤ 366 gün, klinik kapsamı, sayfalama, Sıra No, CSV UTF-8 BOM `;`, XLSX, 50.000 satır tavanı, aynı okuyucu çağrısı) aynen kullanılır.

| Liste | JSON | CSV | XLSX | İzin |
|---|---|---|---|---|
| HBS takibi — reçeteler | `GET /reports/registers/hbs-prescriptions` | `…/export` | `…/export-xlsx` | `Prescriptions.Read` |
| HBS takibi — aşılar | `GET /reports/registers/hbs-vaccinations` | `…/export` | `…/export-xlsx` | `Vaccinations.Read` |

Ek sorgu parametreleri: `hbsStatus` (`NotEntered` \| `Entered` \| `NotRequired`; **verilmezse `NotEntered`** = "HBS'e girilmedi" iş listesi), `veterinarianId` (geliş sorumlu hekimi, §4 kuralı; kiracıda olmayan değer hata vermez, eşleşmez). Tarih: reçetede `prescribedAtUtc`, aşıda `appliedAtUtc`. Sıra: tarih, oluşturulma, kimlik artan. Aşıda yalnız takip kapsamı (§1). Sahip adı/telefonu `Clients.Read` istemez (defter ilkesi); kimlik no **taşınmaz**.

`HbsPrescriptionRegisterItemDto` (dosya sütunu): `rowNumber` (Sıra No), `prescriptionId` (dosyada yok), `prescribedAtUtc` (Reçete Tarihi), `ownerName` (Hasta Sahibi), `ownerPhone` (Telefon), `petName` (Hasta), `species` (Tür), `microchipNumber` (Çip No), `hbsAnimalId` (HBS Hayvan Kimlik No), `title` (Reçete), `medications` (İlaçlar: `Ad ×Miktar Birim` `; ` ile; satırsız reçetede `Content`), `veterinarianName` (Veteriner Hekim), `hbsStatus` (HBS Durumu: Girilmedi/Girildi/Gerekmiyor), `referenceNumber` (e-Reçete No), `markedAtUtc` (HBS İşaret Tarihi).

`HbsVaccinationRegisterItemDto`: `rowNumber`, `vaccinationId` (dosyada yok), `appliedAtUtc` (Uygulama Tarihi), `ownerName`, `ownerPhone`, `petName`, `species`, `microchipNumber`, `hbsAnimalId`, `vaccineName` (Aşı), `lotNumber` (Seri No), `expiryDate` (S.K.T), `veterinarianName`, `hbsStatus`, `referenceNumber` (ATS Belge Seri No), `markedAtUtc`.

Dosya adları `hbs-receteler-yyyyMMdd`, `hbs-asilar-yyyyMMdd` (`.csv|.xlsx`); XLSX sayfa adları "HBS Reçeteler", "HBS Aşılar". Sütunlar tek tanım listesinde (`HbsPrescriptionRegisterColumns`, `HbsVaccinationRegisterColumns`).

---

## 7) Bugün açık işler

`GET /visits/today` → `openItems` (`OPEN_ITEMS_API_CONTRACT.md` madde 4) iki alan kazanır:

| Alan | Tip | Tanım | Görme izni |
|---|---|---|---|
| `hbsNotEnteredPrescriptionCount` | int? | Klinikte `NotEntered` reçete sayısı, reçete tarihi son 30 İstanbul günü | `Prescriptions.Read` |
| `hbsNotEnteredVaccinationCount` | int? | Klinikte takipteki `NotEntered` aşı sayısı, uygulama tarihi son 30 İstanbul günü | `Vaccinations.Read` |

Pencere `[bugünün İstanbul başlangıcı − 30 gün, şimdi)`; sabit `OpenItemsDefaults.HbsLookbackDays = 30`. Gerekçe: bu özellik öncesi tüm eski kayıtlar `NotEntered` başlar; sınırsız sayım sahte iş yükü gösterir (ücret yok sayımı ile aynı ilke). Gün sonu raporu bu alanları doldurmaz (`null`). Sayım `IVisitOpenItemsReader.GetSummaryAsync` içindedir (tek yer), kapsam ifadesi `HbsTracking`'dendir.

---

## 8) Veri modeli ve migration

`Prescriptions` ve `Vaccinations`: `HbsStatus` (int, varsayılan 0 = NotEntered), `HbsReferenceNumber` (nvarchar 50), `HbsNotRequiredReason` (nvarchar 500), `HbsMarkedAtUtc` (datetime2?), `HbsMarkedByUserId` (uniqueidentifier?); indeks `(TenantId, ClinicId, HbsStatus)`. `PrescriptionItems`: `Dose` (100), `Unit` (30), `LotNumber` (100). Mevcut kayıtlar `NotEntered` başlar (backfill yok). Query DB okuma modelleri ve outbox anlık görüntüleri HBS alanlarını taşımaz.

## 9) Bilinen eksikler / açık

- HBS ekran alanları pilot hekimle doğrulanmadı (ADR-036 Dilim 0 kullanıcı eylemi); alan listesi `HbsDataBuilder`'da tek yerde, doğrulanınca yalnız orası değişir.
- e-Reçete No / ATS seri no biçimi bilinmiyor; yalnız uzunluk sınırı var, benzersizlik zorlanmıyor.
- Reçetede hekim alanı yok (muayenesiz reçetede hekim boş; `REGISTERS_API_CONTRACT.md` bölüm 7).
- Muayene defterindeki "e-Reçete Kimlik" sütunu hâlâ boş (bu alandan doldurulması ayrı iş).
