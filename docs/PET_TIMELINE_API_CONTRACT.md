# Hasta özeti ve zaman çizelgesi (Pet summary / timeline) API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: `docs/ASAMA4_DISCOVERY.md` §5 dilim A, B, C, F; kullanıcı kararları D2, D3, D4 (2026-10-11). Hata zarfı standart `ProblemDetails` + `extensions.code`. **Migration yoktur**; yeni izin yoktur (mevcut `*.Read` izinleri kullanılır). Tüm uçlar komut (Command) DB'den okur.

## Kararlar (özet)

| # | Karar | Sonuç |
|---|---|---|
| D2 | Kilo geçmişi | `Examination.WeightKg` + `VitalsMeasuredAtUtc`'den **türetilir**; ek tablo yok. `Pet.Weight` ayrı, tarihsiz "profil kilosu" olarak kalır. |
| D3 | "Veri alınamadı" | **Tek özet ucu**, bölüm başına `state` ve kısmi yanıt. "Kayıt yok" (`Empty`), "yetki yok" (`Forbidden`), "okunamadı" (`Unavailable`) ayrıdır. |
| D4 | Timeline kapsamı | muayene, tedavi, reçete, lab, **uygulanan** aşı, yatış (tek olay), geliş (Visit; randevu Visit içine katlanır). **Ödeme sonra eklenir** (Aşama 3 sonrası). İptal/yanlış işaretli kayıtlar varsayılan gizli, filtreyle açılır. |
| — | Sayfalama | Opak imleç; offset ve toplam sayım **yok**. |

Ortak kurallar:

- **Klinik kapsamı** `history-summary` ile aynıdır: aktif klinik bağlamı (veya `clinicId` sorgu parametresi) varsa o klinik; yoksa kullanıcının erişebildiği klinikler (tenant geneli roller için hepsi). Hayvan ve müşteri tenant geneli olduğundan hayvan başka klinikte görülmüş olsa da bulunur; yalnız klinik kayıtları süzülür. Erişilebilir klinik yoksa tüm klinik bölümler/kayıtlar boştur.
- **Tür bazlı izin** (aşağıdaki tablo): uçlar `Pets.Read` ister; ek olarak her kayıt türü kendi `*.Read` iznini gerektirir. İzni olmayan tür sorguya **hiç girmez**.

| Tür | Gereken izin |
|---|---|
| `Visit` | `Visits.Read` |
| `Examination` | `Examinations.Read` |
| `Treatment` | `Treatments.Read` |
| `Prescription` | `Prescriptions.Read` |
| `LabResult` | `LabResults.Read` |
| `Vaccination` | `Vaccinations.Read` |
| `Hospitalization` | `Hospitalizations.Read` |
| `Administration` | `Administrations.Read` |
| `Appointment` (yalnız `history-summary`) | `Appointments.Read` |
| `Payment` (yalnız `history-summary`) | `Payments.Read` |

- Hata kodları: `Pets.NotFound` 404; `Tenants.ContextMissing` / `Clinics.NotFound` / `Clinics.AccessDenied` mevcut çözümleyici davranışı; `Pets.Validation` 400 (geçersiz parametre/imleç).

---

## 1. `GET /api/v1/pets/{id}/summary` — hasta özet kartı (dilim A)

İzin: `Pets.Read`. Parametre: `clinicId` (opsiyonel, aktif klinik bağlamını geçersiz kılar; scope kuralı yukarıdaki gibi).

Yanıt (`200`):

```json
{
  "petId": "…",
  "petAlerts": { "flags": ["Allergy"], "note": "Penisiline alerji" },
  "profileWeightKg": 4.2,
  "status": "Active",
  "activeHospitalization": { "state": "Ok", "data": { "id": "…", "clinicId": "…", "clinicName": "…", "admittedAtUtc": "…", "plannedDischargeAtUtc": null, "reason": "…", "examinationId": null } },
  "vaccinations": { "state": "Ok", "data": {
      "lastApplied": { "id": "…", "vaccineName": "…", "appliedAtUtc": "…", "clinicId": "…" },
      "nextDue": { "id": "…", "vaccineName": "…", "dueAtUtc": "…", "isOverdue": false, "clinicId": "…" } } },
  "latestWeight": { "state": "Ok", "data": { "weightKg": 4.4, "measuredAtUtc": "…", "source": "Examination", "examinationId": "…" } },
  "lastVisit": { "state": "Ok", "data": { "id": "…", "arrivedAtUtc": "…", "careStatus": "Completed", "clinicId": "…", "clinicName": "…" } },
  "lastExamination": { "state": "Ok", "data": { "id": "…", "examinedAtUtc": "…", "visitReason": "…", "clinicId": "…", "clinicName": "…" } }
}
```

Bölüm zarfı: `{ "state": "Ok" | "Empty" | "Forbidden" | "Unavailable", "data": … }`.

| `state` | Anlamı | `data` |
|---|---|---|
| `Ok` | Kayıt var | dolu |
| `Empty` | Kayıt **yok** (sorgu başarılı) | `null` |
| `Forbidden` | Kullanıcıda ilgili `*.Read` izni yok | `null` |
| `Unavailable` | Bölüm sorgusu hata verdi; diğer bölümler yine döner | `null` |

- `petAlerts` ve `profileWeightKg` `Pets.Read` altındadır ve bölüm zarfı taşımaz (ayrı sorgu değil, hayvan kaydıyla gelir). Uyarı yoksa `flags: []` ("uyarı yok"); `petAlerts` null olmaz. `profileWeightKg` tarihsizdir (`Pet.Weight`), tarih uydurulmaz.
- `activeHospitalization`: `DischargedAtUtc == null` olan kayıt (`history-summary`'deki son 10 sınırından bağımsız, ayrı sorgu). Birden fazla klinikte aktif yatış nadir de olsa mümkünse en yeni `AdmittedAtUtc` döner. İzin: `Hospitalizations.Read`.
- `vaccinations` (`Vaccinations.Read`): `lastApplied` = `Status=Applied` olanlar içinde en yeni `AppliedAtUtc`; `nextDue` = `Status=Scheduled` olanlar içinde **en erken** `DueAtUtc` (gecikmiş olabilir: `isOverdue = dueAtUtc <` bugünün İstanbul gün başı, ADR-028 — bugün vadeli aşı gecikmiş değildir; **vefat eden hayvanda hep `false`**, ADR-020 S5 — kayıt yine görünür). İkisi ayrı alandır; ikisi de yoksa `state: "Empty"`, biri varsa `Ok` ve diğeri `null`.
- `latestWeight` (`Examinations.Read`): `WeightKg` dolu muayeneler içinde `VitalsMeasuredAtUtc` (yoksa `ExaminedAtUtc`) en yeni olan. `source` bugün hep `"Examination"`.
- `lastVisit` (`Visits.Read`): yanlış işaretli (`VoidedAtUtc != null`) olmayan en yeni `ArrivedAtUtc`.
- `lastExamination` (`Examinations.Read`): en yeni `ExaminedAtUtc`.
- `status` (`Pets.Read`, bölüm zarfı yok): `Active` | `Deceased`; eklenen alan, geriye uyumlu (ADR-020 S5). Ayrıntı: `PET_STATUS_CLIENT_CONTACT_API_CONTRACT.md`.
- Açık iş göstergesi bu yanıtta yoktur; hasta kartı "Yapılacaklar" için ayrı uç: `GET /pets/{id}/open-items` (`PET_OPEN_ITEMS_API_CONTRACT.md`).

## 2. `GET /api/v1/pets/{id}/timeline` — birleşik zaman çizelgesi (dilim B)

İzin: `Pets.Read` + tür bazlı izinler.

Sorgu parametreleri (hepsi opsiyonel):

| Parametre | Tip | Anlamı |
|---|---|---|
| `types` | string, tekrarlı (`types=Visit&types=Examination`) | Yalnız bu türler. Bilinmeyen veya henüz desteklenmeyen ad (`Payment`, `Appointment`) → `400 Pets.Validation`. Boş = yetkili tüm türler |
| `fromUtc` / `toUtc` | ISO-8601 | Olay tarihi aralığı: `fromUtc <= eventAtUtc < toUtc` (yarı açık). `fromUtc >= toUtc` → `400 Pets.Validation` |
| `clinicId` | Guid | Klinik süzgeci (scope kuralı yukarıdaki gibi; erişilemeyen klinik `Clinics.AccessDenied`) |
| `includeVoided` | bool, varsayılan `false` | `true`: yanlış işaretli geliş ve iptal edilmiş aşı da döner (`isVoided=true`) |
| `limit` | int, varsayılan 20, **en çok 50** | Sayfa boyutu. Aralık dışı → `400 Pets.Validation` |
| `cursor` | string | Önceki yanıttaki `nextCursor` (opak) |

Yanıt (`200`):

```json
{
  "items": [
    {
      "type": "Visit",
      "sourceId": "…",
      "eventAtUtc": "2026-10-11T08:30:00Z",
      "clinicId": "…",
      "clinicName": "…",
      "title": "Geliş",
      "summary": null,
      "status": "Completed",
      "isVoided": false,
      "linkRef": { "type": "Visit", "id": "…" },
      "relatedExaminationId": null,
      "relatedVisitId": null,
      "relatedAppointmentId": "…",
      "addedAfterFinalization": false,
      "updatedAtUtc": null,
      "relatedTreatmentId": null
    }
  ],
  "nextCursor": "opak-metin-veya-null",
  "omittedTypes": ["Payment"]
}
```

Alanlar:

- `type`: `Visit`, `Examination`, `Treatment`, `Prescription`, `LabResult`, `Vaccination`, `Hospitalization` (string enum; ödeme sonradan `Payment` olarak eklenir, geriye uyumlu).
- `eventAtUtc` türe göre: Visit `ArrivedAtUtc`; Examination `ExaminedAtUtc`; Treatment `TreatmentDateUtc`; Prescription `PrescribedAtUtc`; LabResult `ResultDateUtc`; Vaccination (uygulanan) `AppliedAtUtc` — iptal edilmiş aşı için `DueAtUtc ?? CreatedAtUtc`; Hospitalization `AdmittedAtUtc` (**tek olay**; taburcu `status` ve `summary` içinde, ayrı olay değildir).
- `title`: Visit `"Geliş"`; Examination `VisitReason`; Treatment/Prescription `Title`; LabResult `TestName`; Vaccination `VaccineName`; Hospitalization `Reason`.
- `status` (nullable string): Visit `Waiting`/`InProgress`/`Completed`; Hospitalization `Active`/`Discharged`; Vaccination `Applied`/`Cancelled`; diğerlerinde `null`.
- `summary`: kısa ek metin (nullable). Bugün yalnız acil gelişte `"Acil"`; diğer türlerde `null`. Klinik serbest metin (bulgu, sonuç, reçete içeriği) **timeline'a konmaz**; ayrıntı için `linkRef` ile kaynak uca gidilir.
- `isVoided`: yanlış işaretli geliş veya iptal aşı; varsayılan olarak gizlidir, `includeVoided=true` ile gelir.
- `linkRef { type, id }`: kaynak kaydı ifade eder; **backend URL üretmez**, frontend `type` → rota eşlemesi yapar.
- `relatedExaminationId`: Treatment/Prescription/LabResult/Hospitalization/Vaccination için bağlı muayene (yoksa `null`); Examination satırında `null`. **Visit satırında** gelişe bağlı muayene (`Examination.VisitId`): birden fazla muayene bağlıysa **en yenisi** (`ExaminedAtUtc` DESC, eşitse `Id` DESC); bağlı muayene yoksa `null`. Muayene bağı klinik kapsamına uyar (kapsam dışı klinikteki muayene sayılmaz). Kullanıcıda `Examinations.Read` yoksa Visit satırında **her zaman `null`** döner (izin süzmesiyle tutarlı; muayene kimliği sızmaz). Doldurma `types` süzgecinden bağımsızdır (`types=Visit` ile de gelir). Gelişin tüm muayeneleri için istemci `relatedVisitId` ile Examination satırlarını gruplar.
- `relatedVisitId`: yalnız Examination; muayenenin bağlı olduğu geliş (`Examination.VisitId`, yoksa `null`). Muayeneleri geliş altında gruplamak istemcidedir.
- `relatedAppointmentId`: yalnız Visit; randevulu gelişte randevu kimliği (randevu **ayrı satır olmaz**, Visit içine katlanır). Randevunun içeriği döndürülmez (`Appointments.Read` gerektirir).
- `relatedTreatmentId` (Guid?, TIMELINE-004 ile eklendi, geriye uyumlu): yalnız Prescription ve Administration; kaydın bağlı olduğu tedavi (`TreatmentId`, yoksa `null`). Diğer türlerde her zaman `null`. `relatedExaminationId` gibi kimlik taşır, tedavinin içeriğini döndürmez (`Treatments.Read` ile `GET /treatments/{id}`). Muayeneye değil yalnız tedaviye bağlı uygulama ve reçetenin kaynağına giden tek bağdır.
- `updatedAtUtc`: kayıt güncelleme tarihi (varsa); Visit için `null`.
- **`Administration` (uygulama kaydı, ADR-013):** aşı kaydına bağlı uygulama (`vaccinationId` dolu) **bu türde gösterilmez**, olay `Vaccination` satırıdır (çift satır yok). `Administrations.Read` ister; izin yoksa `omittedTypes` içine düşer. `eventAtUtc` = `administeredAtUtc`; `title` = ürünün adı; `summary` = `"<miktar> <birim>"` (ör. `"1.5 şişe"`; serbest klinik metin değil); `status` = `Active` | `Voided`; `relatedExaminationId` = uygulamanın bağlı muayenesi (yoksa `null`); `linkRef` = `{ type: "Administration", id }`. İptal edilenler varsayılan gizli, `includeVoided=true` ile `isVoided=true` olarak gelir. Klinik kapsamı diğer türlerle aynıdır.
- `addedAfterFinalization` (bool): yalnız `Administration` satırlarında anlamlıdır; bağlı muayene kayıt anında kesinleşmişse `true` ("Kesinleştirmeden sonra eklendi" rozeti). Diğer türlerde her zaman `false`. Gerekçe (`lateEntryReason`) timeline'a konmaz; `GET /administrations/{id}` ile alınır.
- `omittedTypes`: **yetki yok** nedeniyle sorguya girmeyen türler. İstenen `types` içinden izni olmayanlar da buraya düşer. Boş liste = hiçbir tür yetkisizlikten çıkarılmadı. Böylece "kayıt yok" (boş `items`, tür `omittedTypes`'ta değil) ile "yetki yok" ayrılır.

Sıralama ve sayfalama:

- Sıra `(eventAtUtc DESC, typeOrder DESC, sourceId DESC)`; `typeOrder` türün sabit sıra numarasıdır (Visit=1, Examination=2, Treatment=3, Prescription=4, LabResult=5, Vaccination=6, Hospitalization=7, Administration=10 (8 ve 9 `Appointment`/`Payment` için ayrılmıştır); yeni tür sona eklenir, sayılar değişmez). Eşit tarihte bile sıra deterministiktir.
- `cursor` opak (base64) bir değerdir; istemci içeriğini yorumlamamalı, değiştirmemelidir. Bozuk imleç → `400 Pets.Validation`. `nextCursor` null ise sayfa sonudur. Toplam sayı ve sayfa numarası **yoktur**.
- Aynı süzgeçle aynı imleç zinciri kayıpsız ve çiftsiz ilerler. **Kayma riski (kabul edilmiş):** `ExaminedAtUtc`, `TreatmentDateUtc` gibi olay tarihleri kullanıcı tarafından düzenlenebilir; sayfalar arası düzenlenen kayıt, yeni tarihine göre başka sayfaya geçebilir. İmleç değer tabanlı olduğundan ekleme/silme imleci bozmaz.
- Sunucu süzgecini her sayfada yeniden uygular; `types`, tarih aralığı, `includeVoided` ve `clinicId` imleç zinciri boyunca aynı gönderilmelidir.

## 3. `GET /api/v1/pets/{id}/history-summary` değişiklikleri (dilim C ve F)

Uç **uyumluluk için kalır**; değişiklikler geriye uyumludur (yalnız alan eklenir; var olan alan adları/tipleri değişmez).

**C — eklenen alanlar** (ilgili `…ItemDto` kayıtlarının sonuna):

| Blok | Yeni alan |
|---|---|
| `recentExaminations[]` | `visitId` (Guid?), `updatedAtUtc` (DateTime?) |
| `recentTreatments[]` | `updatedAtUtc` |
| `recentPrescriptions[]` | `updatedAtUtc` |
| `recentLabResults[]` | `updatedAtUtc` |
| `recentHospitalizations[]` | `examinationId` (Guid?), `updatedAtUtc` |

Randevu ve ödeme entity'lerinde güncelleme tarihi yoktur; bu bloklara alan eklenmedi.

**F — tür bazlı izin süzmesi:** uç `Pets.Read` ister; her blok kendi `*.Read` iznine bağlanır (yukarıdaki tablo). İzni olmayan blok **boş liste** döner ve yanıta `omittedBlocks` eklenir (string dizisi: `"Appointments"`, `"Examinations"`, `"Treatments"`, `"Prescriptions"`, `"LabResults"`, `"Hospitalizations"`, `"Payments"`). Boş blok + `omittedBlocks`'ta yok = kayıt yok; `omittedBlocks`'ta var = yetki yok. Tüm izinlere sahip kullanıcıda `omittedBlocks: []` ve davranış önceki sürümle aynıdır.

## 4. `GET /api/v1/pets/{id}` değişikliği

Değişiklik yok (`petAlerts` zaten var, `docs/PET_ALERTS_API_CONTRACT.md`).

## 5. Bilinen sınırlar

- Okuma Command DB'den yapılır; Query DB read modeli yok (bayraklar kapalı). Her tabloda `(TenantId, PetId)` indeksi vardır; `(TenantId, PetId, Tarih)` bileşik indeksi yoktur. Hayvan başına satır sayısı az olduğu varsayılır (ölçülmedi); sorun çıkarsa hedefli indeks migration'ı ayrı onayla.
- Bir sayfada en çok `7 × (limit + 1)` satır okunur.
- Ödeme türü timeline'a Aşama 3 (Payments/Charges) oturunca eklenecek; kaynak-okuyucu arayüzü sayesinde mevcut yanıt şekli değişmez, yalnız `type: "Payment"` ve `omittedTypes` içinde `Payment` olasılığı eklenir. Bugün ödeme `omittedTypes`'ta **görünmez** (henüz desteklenen tür değildir).
- Muayene `Status` (Taslak/Kesinleşmiş) yazma grubunda ayrı dalda ilerler; geldiğinde Examination satırının `status` alanı dolacaktır (şimdi `null`).
- Gerçek zamanlı olmayan okuma: iki istek arasında kayıt eklenirse imleç zincirinde yeni kayıt (daha yeni tarihli) görünmeyebilir; sayfa 1'i yenileyerek alınır.
