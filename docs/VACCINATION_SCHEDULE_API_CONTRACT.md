# Otomatik aşı sihirbazı API sözleşmesi — TIER-005

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu backlog `TIER-005`, ADR-035 (2026-11-06 paket yönü). İlgili: `PLAN_FEATURES_API_CONTRACT.md`, `AUTOMATED_REMINDERS_API_CONTRACT.md` (aşı hatırlatması bu işin dışında), `IMPORT_API_CONTRACT.md` §4.3 (5 yıl plan sınırı).

> **Durum:** Uygulandı (kod + test), dal `feat/tier-005-asi-sihirbazi`. Migration **`AddVaccinationScheduleItems`** (yalnız yerel DB). Yeni izin yok. §6'daki maddeler **çalışma varsayımıdır, kullanıcı onayı bekliyor.**

---

## 1) Kapsam

Paket: Profesyonel ve Kurumsal (`PlanFeature.VaccinationWizard`). Bugünkü aşı kaydı, planlı aşı, aşı listesi, Aşı Kayıt Defteri ve aşı hatırlatması her pakette aynen kalır.

| Parça | Ne yapar |
|---|---|
| Aşı takvimi | Kiracı başına; tür + aşı satırları: ilk doz yaşı, temel seri doz sayısı ve aralığı, tekrar aralığı |
| Öneri | Hayvanın türü, doğum tarihi ve uygulanmış dozlarından kalan dozları hesaplar; kayıt oluşturmaz |
| Onay | Hekimin seçtiği (tarihini değiştirebildiği) dozlar mevcut **planlı aşı kaydı** olur (`Vaccination`, `Scheduled`); paralel aşı modeli yoktur |
| Başlangıç şablonu | Yalnız klinik isteyince yüklenir; yüklenen satırlar sıradan takvim satırıdır, düzenlenir ve silinir |

Kuralların tek yeri: takvim satırı ve kalan doz hesabı `VaccinationScheduleItem` (`PlanDoses`); paket kapısı `VaccinationWizardAccess`; aşı tanımı ve tür uyumu `VaccinationCatalogResolver`; planlı kaydın tarih kuralları `CreateVaccinationCommand` (yeniden kullanılır).

## 2) Takvim

### Satır

| Alan | Kural |
|---|---|
| `speciesId` | Aktif tür; sonradan değişmez |
| `vaccineDefinitionId` | Görünür (sistem ya da kiracı), aktif, türe uyan aşı tanımı; sonradan değişmez |
| `startAgeWeeks` | 0–520. İlk doz doğum tarihi + bu hafta |
| `doseCount` | 1–5. Temel seri (ilk doz dahil) |
| `doseIntervalWeeks` | 1–52; `doseCount > 1` ise zorunlu, tek dozda `null` yazılır |
| `repeatIntervalMonths` | `null` (tekrar yok) ya da 1–60 |
| `notes` | En fazla 500 karakter |

Kiracı başına tür + aşı tektir. Satır silinince daha önce plandan oluşmuş aşı kayıtları etkilenmez.

### Uçlar

| Uç | İzin | Paket |
|---|---|---|
| `GET /api/v1/vaccination-schedule/items?speciesId=` | `VaccineDefinitions.Read` | Paketten bağımsız |
| `POST /api/v1/vaccination-schedule/items` | `VaccineDefinitions.Update` | İster |
| `PUT /api/v1/vaccination-schedule/items/{id}` | `VaccineDefinitions.Update` | İster |
| `DELETE /api/v1/vaccination-schedule/items/{id}` | `VaccineDefinitions.Update` | İster |
| `POST /api/v1/vaccination-schedule/starter-template` | `VaccineDefinitions.Update` | İster |

`GET` yanıtı (tür sırası, başlangıç yaşı, aşı adı sırasıyla):

```json
{ "items": [
  { "id": "…", "speciesId": "…", "speciesName": "Köpek", "vaccineDefinitionId": "…", "vaccineName": "Karma", "vaccineIsActive": true,
    "startAgeWeeks": 6, "doseCount": 3, "doseIntervalWeeks": 3, "repeatIntervalMonths": 12, "notes": null, "updatedAtUtc": null } ] }
```

`POST` ve `PUT` gövdesi `{ speciesId, vaccineDefinitionId, startAgeWeeks, doseCount, doseIntervalWeeks, repeatIntervalMonths, notes }`; yanıt tek satır (yukarıdaki şekil). `PUT`'ta `speciesId` ve `vaccineDefinitionId` yok sayılır. `DELETE` 204.

`starter-template` yanıtı `{ "addedCount": 11, "schedule": { "items": [ … ] } }`: yalnız takvimde olmayan tür + aşı eklenir, mevcut satır değişmez; ikinci çağrı `addedCount: 0`. Sistem kataloğunda bulunmayan ya da pasif aşı/tür atlanır.

Denetim: `VaccinationSchedule.Item.Create|Update|Delete`, `VaccinationSchedule.StarterTemplate.Load`.

## 3) Öneri — `GET /api/v1/vaccination-schedule/pets/{petId}/plan` (`Vaccinations.Read`, paket ister)

```json
{ "petId": "…", "petName": "Fındık", "speciesId": "…", "speciesName": "Köpek", "birthDate": "2026-09-20", "scheduleItemCount": 6,
  "suggestions": [
    { "scheduleItemId": "…", "vaccineDefinitionId": "…", "vaccineName": "Karma", "doseNumber": 1, "doseCount": 3,
      "appliedDoseCount": 0, "dueDate": "2026-11-09", "isLate": true },
    { "…": "…", "doseNumber": null, "dueDate": "2027-12-21", "isLate": false } ],
  "skipped": [ { "scheduleItemId": "…", "vaccineDefinitionId": "…", "vaccineName": "Kuduz", "reason": "AlreadyScheduled", "dueDate": "2026-12-13" } ] }
```

| Konu | Kural |
|---|---|
| Satırlar | Hayvanın türündeki takvim satırları |
| Uygulanmış doz | Aynı aşı tanımının `Applied` kayıtları; kullanıcının okuyabildiği kliniklerden (aşı listesiyle aynı okuma kuralı, tenant geneli rolde tüm klinikler). İptal ve katalogsuz kayıt sayılmaz |
| Temel seri bitmedi | İlk kalan doz: hiç doz yoksa doğum tarihi + başlangıç yaşı (doğum tarihi yoksa en erken gün), varsa son doz + doz aralığı. Sonraki dozlar bir öncekinden aralıkla; ardından (varsa) bir tekrar |
| Temel seri bitti | Yalnız sıradaki tekrar: son doz + tekrar aralığı; tekrar yoksa `Completed` |
| En erken gün | Yarın (İstanbul günü). Daha önceye düşen ilk doz yarına alınır, `isLate: true`; sonraki dozlar kaydırılmış tarihten sayılır |
| Ufuk | 5 yıldan sonraki doz önerilmez (planlı aşı sınırı); hepsi dışarıda kalırsa `BeyondHorizon` |
| `doseNumber` | Temel seri doz numarası; `null` = tekrar |
| Sıralama | Öneriler tarihe, sonra aşı adına göre |

`skipped.reason`:

| Değer | Anlamı |
|---|---|
| `AlreadyScheduled` | Bu aşının planlı kaydı var (gecikmiş dahil); `dueDate` en erken planlı tarih |
| `NextDueSet` | Son uygulanan dozda ileri tarihli "sonraki uygulama" girilmiş; hatırlatma o kayıttan gider, çift kayıt açılmaz |
| `Completed` | Temel seri bitti, tekrar yok |
| `VaccineInactive` | Aşı tanımı pasif |
| `BeyondHorizon` | Kalan doz 5 yıldan sonra |

## 4) Onay — `POST /api/v1/vaccination-schedule/pets/{petId}/plan` (`Vaccinations.Create`, paket ister)

Gövde `{ "clinicId": "…", "items": [ { "vaccineDefinitionId": "…", "dueDate": "2026-11-09", "notes": "Aşı takviminden: 1. doz / 3" } ] }`. Yanıt `{ "petId": "…", "created": [ { "vaccinationId": "…", "vaccineDefinitionId": "…", "dueDate": "2026-11-09" } ] }`.

- Her satır `CreateVaccinationCommand` ile `Scheduled`, `appliedAtUtc: null`, `dueAtUtc` = o günün İstanbul 12:00'si (UTC) olarak yazılır; klinik kapsamı, aşı tanımı ve tür uyumu, tarih kuralları ve 5 yıl sınırı orada denetlenir.
- Satırlar takvimde olmak zorunda değildir: hekim öneriyi değiştirebilir.
- **Tümü ya da hiçbiri:** bir satır reddedilirse önceki satırlar geri alınır (`ITransactionalRollbackOnFailureRequest`).
- Denetim `VaccinationPlan.Apply`, hedef `PetId=…`. Salt okunur kiracıda yazma kapısı geçerlidir.
- Oluşan kayıtlar bugünkü planlı aşı gibi listede, hasta kartında, portalda ve aşı hatırlatmasında görünür.

| Kod | HTTP | Durum |
|---|---|---|
| `Subscriptions.FeatureNotInPlan` | 403 | Pakette `VaccinationWizard` yok (`requiredPlanCode: "Pro"`). Öneri ucunda da aynı |
| `Pets.Deceased` | 409 | Hayvan bakım dışı (vefat); öneri ucunda da aynı |
| `Vaccinations.ScheduledDueAtMustNotBePast` | 400 | `dueDate` bugün ya da geçmiş |
| `VaccinationPlans.DuplicateScheduled` | 409 | Aynı aşının aynı güne planlı kaydı var (çift tıklama); hiçbir kayıt yazılmaz |
| `Validation.FluentValidation` | 400 | Boş liste, 20'den fazla satır, aynı aşı + gün iki kez |
| `VaccineDefinitions.NotFound` / `.Inactive` / `.InvalidSpecies` | 404 / 400 / 400 | Satırdaki aşı tanımı (mevcut kurallar) |
| `Pets.NotFound` | 404 | Hayvan bu kiracıda yok |

Takvim uçları: `VaccinationSchedules.Validation` 400 (§2 sınırları), `VaccinationSchedules.InvalidSpecies` 400, `VaccinationSchedules.DuplicateItem` 409, `VaccinationSchedules.NotFound` 404, paket 403.

## 5) Paket düşünce

Takvim silinmez ve okunur; yazma, öneri ve onay 403 olur. Plandan oluşmuş planlı aşılar sıradan aşı kaydıdır, her pakette kalır, uygulanır ve hatırlatılır.

## 6) Çalışma varsayımları, kullanıcı onayı bekliyor

1. Takvim kiracı başınadır (aşı kataloğu gibi); şubeye göre ayrı takvim yok.
2. İzinler mevcut olanlardır: takvim yönetimi `VaccineDefinitions.Update` (Admin, Owner, ClinicAdmin), öneri `Vaccinations.Read`, onay `Vaccinations.Create`. Yeni izin yok.
3. Başlangıç şablonu (kedi ve köpek; iç parazit, karma, corona, bronşin, dış parazit, kuduz, lösemi) yaygın uygulamadan derlenmiş genel bir başlangıçtır, tıbbi tavsiye değildir; değerler `VaccinationScheduleStarterTemplate`'te. Hekim gözden geçirir.
4. Tekrar dozu temel seriden sonra yalnız bir tane önerilir; sonrakini o doz uygulanınca yeni öneri ya da kaydın "sonraki uygulama" tarihi verir.
5. Uygulanmış dozun "sonraki uygulama" tarihi ileri bir tarihse o aşı önerilmez (`NextDueSet`), aynı tarihe iki hatırlatma gitmesin diye.
6. En erken gün yarındır; geç kalan ilk doz yarına alınır.

## 7) Testler

- Domain: `VaccinationScheduleItemTests` (sınırlar, tek doz, seri, kısmi seri, geç kalan doz, doğum tarihi yok, tekrar).
- Application: `TenantPlanFeatureEvaluatorTests` paket tablosu.
- Entegrasyon: `VaccinationScheduleEndpointTests` (Temel'de kilit ve okuma, ad alanları, çift satır, geçersiz değer, öneri doğum tarihinden ve uygulanmış dozdan, onayın planlı kayıt yazması, çift onay, tümü ya da hiçbiri, geçmiş tarih, vefat, başlangıç şablonu).

## 8) Bilinen sınırlar

- Hayvanın yaşı yalnız doğum tarihinden bilinir; doğum tarihi yoksa ilk doz en erken güne önerilir.
- Aşı kaydı ile takvim satırı aşı tanımı kimliğiyle eşleşir; tanımsız (eski, serbest metin) aşı kayıtları sayılmaz.
- Aynı anda iki onay isteği (milisaniye yarışı) aynı dozu iki kez yazabilir; çift tıklama ve yeniden gönderim reddedilir.
