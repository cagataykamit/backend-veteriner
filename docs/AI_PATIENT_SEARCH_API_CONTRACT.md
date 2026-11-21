# Doğal dille hasta araması — API sözleşmesi (AI-006)

Durum: 2026-11-19, dal `feature/ai-dogal-arama`. Ortak AI kuralları (izin, klinik açma, kota, hata kodları):
[AI_HISTORY_SUMMARY_API_CONTRACT.md](AI_HISTORY_SUMMARY_API_CONTRACT.md).

## 1. Akış ve güvenlik

1. İstemci cümleyi `POST /api/v1/ai/pet-search/interpret`'e gönderir. Model cümleyi **yalnız** §2'deki filtre alanlarına çevirir;
   SQL ya da serbest sorgu yazmaz, kayıt görmez. Modele giden: cümle (e-posta ve 10+ haneli numaralar maskeli), bugünün tarihi
   (İstanbul), aktif tür listesi (kod + ad), kliniğin şikâyet/tanı sözlüğü (sistem + kiracı, aktif, en çok 400 ad). Hasta, müşteri
   ve klinik kayıt verisi gitmez. Cümle saklanmaz, denetime ve günlüğe yazılmaz. Kota harcar (özellik `PetSearch`, istem
   `pet-search/1`).
2. Yanıttaki filtre arayüzde "şöyle anladım" diye gösterilir; kullanıcı düzenleyebilir.
3. İstemci filtreyi `POST /api/v1/ai/pet-search/results`'a gönderir. Bu uç **AI çağırmaz**, kota harcamaz; filtreyi aynı kuralla
   (`PetSearchFilterRules`) yeniden doğrular ve var olan kiracı/klinik/izin kurallarıyla arar.

İki uç da `Ai.Use` + `Pets.Read` ve aktif klinik ister (yoksa 400 `Clinics.ClinicSelectionRequired`). Kliniğin AI'si kapalıysa
`interpret` 403 `Ai.ClinicNotEnabled` döner; `results` yalnız `Ai.Use` ister (kota dolsa da düzenlenen filtre aranabilir).

## 2. Filtre (`PetSearchFilter`)

Tüm alanlar isteğe bağlı; verilen alanlar **VE**, bir listedeki terimler **VEYA** ile birleşir. Bilinmeyen alan yok sayılır;
geçersiz değer hata değildir, atılır ve `dropped[]`'a gerekçesiyle yazılır.

| Alan | Tür | Kural | Aranan yer |
|---|---|---|---|
| `species` | string[] | aktif tür kodu (`CAT`, `DOG`…), en çok 10 | hayvan |
| `sex` | `Male` \| `Female` | | hayvan |
| `neutered` | bool | | hayvan |
| `ageMinYears`, `ageMaxYears` | int 0–40 | tamamlanmış yaş, dahil; ters verilirse yer değişir; doğum tarihi olmayan eşleşmez | hayvan |
| `status` | `Active` \| `Deceased` | | hayvan |
| `from`, `to` | `YYYY-MM-DD` | İstanbul günü, dahil; bugünden 30 yıl geri – 2 yıl ileri; ters verilirse yer değişir | klinik kayıt tarihi |
| `clinicalTerms` | string[] | şikâyet/tanı terimi | muayene: geliş nedeni, anamnez, bulgular, değerlendirme |
| `vaccineTerms` | string[] | aşı adı | aşı adı |
| `vaccineState` | `Any` \| `Applied` \| `Scheduled` \| `Overdue` | terim verilip durum verilmezse `Any`; tek başına da verilebilir | aşı durumu |
| `medicationTerms` | string[] | ilaç / etken madde | reçete kalemi adı, reçete başlığı ve içeriği; tedavi başlığı ve açıklaması |

- Terim: 2–50 karakter; harf, rakam, boşluk, `-`, `'`, `.`; liste başına en çok 5; büyük/küçük harf duyarsız tekilleştirilir. LIKE
  parametresi olarak (kaçışlı, "içinde geçer") kullanılır.
- Tarih aralığı: muayenede muayene tarihine, reçetede reçete tarihine, tedavide tedavi tarihine, aşıda uygulanmışsa uygulama,
  planlıysa vade tarihine bakar. Başka klinik ölçüt yokken yalnız tarih aralığı = "o aralıkta muayenesi olan hayvanlar".
- `Overdue`: planlı ve vadesi son 1 takvim ayı içinde, bugünün (İstanbul) başından önce (`VaccinationDueRule.OverdueWindow`); vefat eden hayvan sayılmaz. Eşleşmedeki `isOverdue` de aynı kuraldır.
- İptal edilmiş aşı, reçete ve tedavi aranmaz.
- Desteklenmeyenler (model `unsupported[]`'a yazar): ad ile arama, olumsuz ölçüt ("aşısı olmayan"), ödeme/borç, lab, yatış, randevu.

## 3. `POST /api/v1/ai/pet-search/interpret`

Gövde `{ "query": "son 3 ayda kusma şikâyetiyle gelen kediler" }` (zorunlu, ≤500 karakter).

```json
{
  "filter": { "species": ["CAT"], "from": "2026-08-19", "to": "2026-11-19", "clinicalTerms": ["kusma"] },
  "isEmpty": false,
  "unsupported": [],
  "dropped": [],
  "model": "claude-haiku-5-5",
  "interpretedAtUtc": "2026-11-19T10:00:00Z"
}
```

`filter`'da yalnız dolu alanlar anlamlıdır (boşlar `null`). `isEmpty: true` → hiçbir ölçüt anlaşılamadı, arama yapılamaz.

Hatalar: 400 `AiSearch.Validation` (boş cümle) ve ortak AI hataları (403 `Ai.ClinicNotEnabled`, 429 `Ai.RateLimited` /
`Ai.ClinicQuotaExceeded`, 503 `Ai.NotConfigured` / `Ai.SystemBudgetExhausted` / `Ai.ProviderUnavailable`, 502
`Ai.ProviderRejected` / `Ai.InvalidOutput`, 422 `Ai.Refused`).

## 4. `POST /api/v1/ai/pet-search/results`

Gövde `{ "filter": { … }, "page": 1, "pageSize": 25 }` (`pageSize` 1–100).

```json
{
  "filter": { "…uygulanan filtre…": "" },
  "dropped": [],
  "items": [
    {
      "petId": "…", "name": "Pamuk", "speciesCode": "CAT", "speciesName": "Kedi", "breed": null,
      "sex": "Female", "isNeutered": true, "ageYears": 3, "status": "Active",
      "clientId": "…", "clientName": "…",
      "matches": [ { "type": "Examination", "id": "…", "occurredAtUtc": "…", "text": "Kusma", "isOverdue": false } ]
    }
  ],
  "totalCount": 1, "page": 1, "pageSize": 25,
  "truncated": false,
  "omittedTypes": []
}
```

- **Kapsam:** klinik ölçütler yalnız aktif kliniğin kayıtlarında (`IClinicReadScopeResolver`); hayvan ölçütleri kiracının
  hayvanlarında (hasta listesiyle aynı). Kiracı filtresi her sorguda.
- **İzin:** `clinicalTerms` (ya da yalnız tarih aralığı) `Examinations.Read`, aşı ölçütü `Vaccinations.Read`, ilaç ölçütü
  `Prescriptions.Read` ya da `Treatments.Read` ister. İzin yoksa ölçüt atılmaz (sonuç genişlerdi): 403 `AiSearch.Forbidden`.
  İlaç ölçütünde iki türden yalnız biri okunabiliyorsa diğeri aranmaz ve `omittedTypes`'a yazılır (`Prescription` / `Treatment`).
- **Sıra:** klinik ölçüt varsa en yeni eşleşen kayıt önce; yoksa ada göre.
- `matches`: tür başına en çok 3, en yeni önce; `type` timeline adlarıyla aynı (`Examination`, `Vaccination`, `Prescription`,
  `Treatment`) → istemcideki kayıt rota eşlemesi kullanılır. Yalnız hayvan ölçütü varsa boş.
- `truncated`: bir ölçütte 2.000'den fazla kayıt eşleşti, en eskileri aranmadı.

Hatalar: 400 `AiSearch.Validation` (filtre boş ya da tüm değerler geçersiz), 403 `AiSearch.Forbidden`, 403 (izin/`Ai.Use` yok).

## 5. Hata kodları

| Kod | HTTP | Ne zaman |
|---|---|---|
| `AiSearch.Validation` | 400 | Boş cümle; ölçüt kalmayan filtre |
| `AiSearch.Forbidden` | 403 | Filtrede okuma izni olmayan kayıt türüne dayanan ölçüt |
