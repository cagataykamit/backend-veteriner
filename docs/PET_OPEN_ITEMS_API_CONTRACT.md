# Hasta açık işleri ("Yapılacaklar") API sözleşmesi — ADR-020 S1

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-020-clinic-staff-centered-patient-card.md` (karar 2c: hasta kartı "Yapılacaklar" paneli). İlgili: `PET_TIMELINE_API_CONTRACT.md` (hasta özeti ve bölüm zarfı), `OPEN_ITEMS_API_CONTRACT.md` (Bugün açık işleri, klinik geneli), `CHARGE_SUGGESTION_API_CONTRACT.md`, `CLIENT_BALANCE_API_CONTRACT.md`, `LAB_RESULT_STATUS_API_CONTRACT.md`, `PET_STATUS_CLIENT_CONTACT_API_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feat/hasta-acik-isler`. **Salt okuma; migration, yeni tablo, yeni izin yok**, seed gerekmez. Değerler mevcut kayıtlardan **hesaplanır**, saklanmaz.

---

## 1) `GET /api/v1/pets/{id}/open-items`

İzin: `Pets.Read` (uç). Parametre: `clinicId` (opsiyonel; aktif klinik bağlamını geçersiz kılar). Okuma komut DB'dendir (para verisi projeksiyondan okunmaz).

Tek istekte hasta kartının "Yapılacaklar" paneli: gecikmiş ve yaklaşan aşılar, sonucu ve yorumu bekleyen lab istekleri, ücret bekleyen kalemler (sayı + toplam), sonraki planlı randevu, açık borç payı. Her satırın eylemi istemcidedir (ilgili mevcut uçlara gider).

### Yanıt (`200`)

```json
{
  "petId": "…",
  "clientId": "…",
  "status": "Active",
  "asOfUtc": "2026-10-14T09:00:00Z",
  "vaccinations": { "state": "Ok", "data": {
      "overdue":  [ { "id": "…", "vaccineName": "Karma", "dueAtUtc": "…", "clinicId": "…" } ],
      "upcoming": [ { "id": "…", "vaccineName": "Kuduz", "dueAtUtc": "…", "clinicId": "…" } ] } },
  "labResults": { "state": "Ok", "data": {
      "awaitingResult":         [ { "id": "…", "testName": "Hemogram", "requestedAtUtc": "…", "resultDateUtc": null, "examinationId": "…", "visitId": "…", "clinicId": "…" } ],
      "awaitingInterpretation": [ { "id": "…", "testName": "Biyokimya", "requestedAtUtc": "…", "resultDateUtc": "…", "examinationId": null, "visitId": null, "clinicId": "…" } ] } },
  "pendingCharges": { "state": "Ok", "data": { "count": 3, "totalAmount": 360.00, "unpricedCount": 1, "currency": "TRY" } },
  "nextAppointment": { "state": "Ok", "data": { "id": "…", "scheduledAtUtc": "…", "durationMinutes": 30, "appointmentType": "Vaccination", "clinicId": "…", "clinicName": "…" } },
  "outstanding": { "state": "Ok", "data": { "petOutstandingAmount": 600.00, "petOpenChargeCount": 1, "clientOutstandingAmount": 650.00, "currency": "TRY" } }
}
```

### Bölüm zarfı (hasta özetiyle aynı kural)

`{ "state": "Ok" | "Empty" | "Forbidden" | "Unavailable", "data": … }` — `Ok`: kayıt var, `data` dolu; `Empty`: açık iş yok (sorgu başarılı) veya erişilebilir klinik yok, `data = null`; `Forbidden`: ilgili okuma izni yok, **sorgu yapılmaz**, `data = null`; `Unavailable`: bölüm sorgusu hata verdi, diğer bölümler yine döner. `Empty` "yok" demektir; `Forbidden` "bilinmiyor" demektir (istemci "yok" göstermez).

`petId`, `clientId` (hayvanın güncel sahibi), `status` (`Active` | `Deceased`) ve `asOfUtc` (hesap anı) `Pets.Read` altındadır, zarf taşımaz.

### Bölümler

| Bölüm | İzin | Kural |
|---|---|---|
| `vaccinations` | `Vaccinations.Read` | `Status = Scheduled` aşılar. `overdue`: `dueAtUtc <` bugünün İstanbul gün başı (alt sınır yok; bugün vadeli aşı saati geçse de gecikmiş değildir). `upcoming`: `bugünün başı ≤ dueAtUtc <` bugün + 30 günün başı (bugün dahil 30 İstanbul takvim günü). Tanım `VaccinationDueRule` (ADR-028). İkisi de en erken vade önce, **sınırsız**. Uygulanmış/iptal edilmiş aşı girmez. İkisi de boşsa `Empty`. **Vefat eden hayvanda bölüm `Empty`** (sorgu yapılmaz; ADR-020 S5, `Pet.ReceivesCare`). |
| `labResults` | `LabResults.Read` | `awaitingResult`: `status = Requested`. `awaitingInterpretation`: sonuçlanmış, yorum gerektiren ve yorumu boş kayıt (liste/detaydaki `awaitingInterpretation = true` ile **aynı kural**, `LabResult.IsAwaitingInterpretation`). İptal edilmiş kayıt girmez. **Tüm kayıtlar** ("son 10" sınırı yok), en eski istek önce. |
| `pendingCharges` | `Charges.Read` | Hayvanın `Pending` ücret önerisi kalemleri (`CHARGE_SUGGESTION_API_CONTRACT.md`). `count`: kalem sayısı; `totalAmount`: fiyatlı kalemlerin `round(quantity × suggestedUnitPrice, 2)` toplamı (ücrete çevrilince yazılacak tutarla aynı kural, `Charge.CalculateAmount`); `unpricedCount`: `suggestedUnitPrice = null` kalem sayısı (tutara **katılmaz**, "fiyat bekliyor" ayrı gösterilmeli). Kalem listesi için mevcut `GET /charge-suggestions?clinicId=…&petId=…`. |
| `nextAppointment` | `Appointments.Read` | `status = Scheduled` ve `scheduledAtUtc ≥ şimdi` en yakın randevu. Saati geçmiş ama hâlâ planlı randevu burada **yoktur**. Vefat eden hayvanda da döner (randevu otomatik iptal edilmez, K4; istemci uyarı gösterip elle iptale yönlendirebilir). |
| `outstanding` | `Charges.Read` | Müşteri bakiyesiyle (`GET /clients/{id}/balance`) aynı okuma: hayvanın **güncel sahibinin** kapsamdaki ücretleri, `Charge` sayaçlarından (iptal edilmiş ücret girmez). `petOutstandingAmount` / `petOpenChargeCount`: bu hayvana bağlı açık (`outstandingAmount > 0`) ücretler; `clientOutstandingAmount`: sahibin toplam açık borcu (bakiye ucundaki `outstandingAmount` ile aynı değer). Sahibin hiç açık ücreti yoksa `Empty`; sahip borçlu ama bu hayvanın payı yoksa `Ok` ve `petOutstandingAmount = 0`. Bağlanmamış (eski) ödemeler mahsup edilmez. |

### Klinik kapsamı

Hasta özetiyle aynı: aktif klinik bağlamı (veya `clinicId`) varsa yalnız o kliniğin kayıtları; yoksa kullanıcının erişebildiği klinikler (kiracı geneli roller için hepsi). Erişilebilir klinik yoksa bütün bölümler `Empty`. Hayvan kiracı genelidir; başka klinikte kayıtlı olsa da bulunur, yalnız kayıtlar süzülür. `outstanding` aynı kapsamı kullanır; bu yüzden bağlamsız `GET /clients/{id}/balance` ile değer aynıdır, Bugün satırındaki tek klinikli `clientOutstandingAmount` ile farklı olabilir.

### Hatalar

| Kod | HTTP | Durum |
|---|---|---|
| `Pets.NotFound` | 404 | Hayvan yok / başka kiracı |
| `Tenants.ContextMissing` | 400 | Kiracı bağlamı yok |
| `Clinics.AccessDenied` | 403 | `clinicId` / bağlam atanmamış klinik |
| — | 403 | `Pets.Read` yok |
| (standart doğrulama hatası) | 400 | Boş `id` (`00000000-…`) |

## 2) Güvenlik ve performans

- Her sorgu `TenantId` ve klinik kapsamıyla süzülür; diğer kiracının verisi görünmez. Bölüm izni yoksa ilgili tablo **sorgulanmaz** (yetkisiz türün varlığı çıkarsanamaz).
- Sabit sayıda sorgu (hayvan, aşı 1, lab 2, kalem 1, randevu 1 + klinik adı 1, ücret 1); N+1 yok. Listeler hayvan başına sınırsızdır (hayvan başına açık aşı/lab sayısı küçük varsayılır). Ücret okuması bakiye ucu gibi sahibin kapsamdaki tüm ücret satırlarını çeker.
- Yeni indeks/migration yok; gerçek veri hacminde ölçüm yapılmadı (bilinen eksik).

## 3) Kararlar (rakip pratiği varsayılan)

| # | Konu | Karar | Gerekçe |
|---|---|---|---|
| OI-1 | Ayrı uç | Özet (`/summary`) genişletilmedi; ayrı `open-items` ucu. | Özet "kim bu hasta", açık işler "ne yapılmalı" sorusudur; panel ayrı yenilenir (ödeme/aşı sonrası) ve özetin tek-kayıt bölümleri bozulmaz. Bölüm zarfı aynı olduğu için istemci tek bileşenle okur. |
| OI-2 | Yaklaşan aşı penceresi | Bugün dahil 30 takvim günü (sabit `GetPetOpenItemsQueryHandler.UpcomingVaccinationDays`; gün sınırı İstanbul, ADR-028). | Rakiplerde (DaySmart "Due/Overdue", E-vet hatırlatma listesi) aylık pencere yaygın; pano 7 günü klinik geneli içindir. |
| OI-3 | Liste sınırı | Aşı ve lab listeleri sınırsız. | Hasta başına açık kayıt az; "son 10" sınırı açık işi gizler (history-summary farkı). |
| OI-4 | Ücret bekleyen tutar | Sunucu toplar (`totalAmount`), fiyatsız kalem ayrı sayılır. | Müşteri kartında istemci toplar (K10); hasta panelinde tek sayı yeterli, yuvarlama ücret kuralıyla aynı kalır. |
| OI-5 | Borç payı | Hayvanın güncel sahibine yazılmış ücretler; önceki sahibe yazılmış ücret bu hayvanın payına girmez. | Borç müşteriye aittir (bakiye kuralı); pay yalnız sahibin borcunun bu hayvana düşen kısmıdır. |
| OI-6 | Vefat eden hayvan | Aşı bölümü `Empty`; lab, ücret, randevu, borç normal döner. | Hatırlatma/gecikme yok (S5); ötanazi günü işleri, borç ve açık randevu kapatılmalıdır (K4, K5). |

## 4) Frontend için

1. Hasta kartı "Yapılacaklar" paneli tek istek: `GET /pets/{id}/open-items` (aktif klinik bağlamıyla). Ödeme, ücret çevirme, lab sonucu/yorumu, aşı uygulama ve randevu değişikliğinden sonra yeniden okunur.
2. Satırlar ve eylemler (öneri): gecikmiş aşı (kırmızı) / yaklaşan aşı → aşı uygula; sonucu bekleyen lab → sonuç gir; yorum bekleyen lab → yorumla (hekim); ücret bekleyen `count` + `totalAmount` (`unpricedCount > 0` ise "n kalem fiyat bekliyor") → Ücretlendir paneli (`GET /charge-suggestions?petId=…`); sonraki randevu; açık borç `petOutstandingAmount` (sahip toplamı `clientOutstandingAmount`) → ödeme al.
3. `Forbidden` bölüm gizlenir veya "yetki yok" gösterilir; `Unavailable` "yüklenemedi, yenile"; `Empty` satır göstermez.
4. `status = Deceased` ise aşı satırı yoktur; açık randevu varsa "randevu iptal edilmeli" uyarısı önerilir.
5. Yeni hata kodu yok.

## 5) Bilinen eksikler

- Saati geçmiş planlı (gelinmemiş, kapatılmamış) randevu bu uçta gösterilmez; yalnız sonraki randevu döner.
- Tamamlanmamış geliş, aktif yatış ve aktif tedavi bu uçta yok (yatış hasta özetinde var).
- Gerçek veri hacminde sorgu ölçümü yapılmadı.
