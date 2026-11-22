# Lab değer satırları API sözleşmesi — REF-001 Dilim 3

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-049-ilac-rehberi-ve-klinik-basvuru.md` (K5, K6), backlog REF-001. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code` (bkz. `VISITS_API_CONTRACT.md`). Lab kaydının durum kuralları `LAB_RESULT_STATUS_API_CONTRACT.md`'dedir; bu belge yalnız eklenenleri anlatır.

> **Durum:** Sözleşme. **Kapsam dışı:** global (tür bazlı) referans aralığı tablosu, kliniğin cihaz başına varsayılan aralığı, lab cihazı entegrasyonu (INT-001), AI lab özeti (AI-105), metin (nitel) değerler ("Pozitif" gibi; serbest metinde kalır).

---

## 1) Kavramlar

- **Değer satırı (`LabResultLine`)**: lab kaydının isteğe bağlı yapılandırılmış parçası: parametre adı, **sayısal** değer, birim, sonuçla gelen referans alt ve üst sınırı. Bir kayıtta en çok **60** satır.
- **Referans aralığı laboratuvara özgüdür ve satırla gelir.** Sunucuda tür bazlı ya da global aralık tablosu **yoktur**; sınır verilmeyen satırın işareti de yoktur.
- **İşaret (`flag`) hesaplanır, elle seçilmez:** değer alt sınırın altındaysa `Low`, üst sınırın üstündeyse `High`, aralıktaysa (sınır dahil) `Normal`. Tek sınır verildiyse yalnız o yön değerlendirilir. İki sınır da yoksa `null`. Bu aritmetik karşılaştırmadır; tanı, doz, tedavi önerisi, "olası teşhis" ya da yüzde üretilmez.
- **Serbest metin sonuç aynen kalır.** `resultText` değişmedi; satırlarla birlikte ya da tek başına yazılabilir.
- **Sonuç = metin ya da en az bir satır.** Kayıt, `resultText` dolu **veya** en az bir satır varsa sonuç taşır; sonuç tarihi (`resultDateUtc`) bu durumda zorunludur, sonuç yokken verilemez. Böylece yalnız satırla girilen kayıt da `Resulted` olur ve `resultText` `null` kalabilir. Sonuçlanmış kayıttan son sonuç (metin ve satırların tümü) silinemez.
- **Aynı parametre:** ad, büyük/küçük harf, art arda boşluk ve Türkçe harf farkı yok sayılarak eşlenir (`TurkishTextKey`; "Glikoz", "GLİKOZ", "glikoz" aynı). Bir kayıtta aynı parametre iki kez yazılamaz.
- **Karşılaştırma (eğilim):** satırın bir önceki değeri, **aynı hastada, aynı klinikte, sonuçlanmış (iptal edilmemiş)** ve sonuç tarihi bu kayıttan önce olan kayıtlardaki **aynı parametre ve aynı birim** satırıdır (birim de yazım farkından bağımsız eşlenir; farklı birimli değerler karşılaştırılmaz). Aynı sonuç tarihinde önce oluşturulan kayıt önceki sayılır.

### Satır alanları

| Alan | Tip | Kural |
|---|---|---|
| `parameterName` | string | Zorunlu, en çok 100; kırpılarak saklanır. |
| `value` | decimal | Zorunlu; mutlak değer 1.000.000.000'dan küçük, en çok 4 ondalık. Negatif olabilir. |
| `unit` | string? | En çok 30. |
| `referenceLow` / `referenceHigh` | decimal? | Değerle aynı sayı kuralı; ikisi de verilirse `referenceLow ≤ referenceHigh`. |

## 2) Yetkiler

Yeni izin **yoktur**. Satırlar lab kaydının parçasıdır: yazma `LabResults.Create` / `LabResults.Update`, okuma ve parametre geçmişi `LabResults.Read`. Paket kısıtı yoktur (ADR-049 K7: her pakette).

## 3) Yazma

### `POST /lab-results`

Gövdeye isteğe bağlı `lines` eklenir; diğer alanlar değişmez.

```json
{
  "clinicId": "guid", "petId": "guid", "testName": "Biyokimya",
  "resultDateUtc": "datetime?", "resultText": "string?",
  "lines": [
    { "parameterName": "ALT", "value": 120, "unit": "U/L", "referenceLow": 10, "referenceHigh": 100 },
    { "parameterName": "Glikoz", "value": 98.5, "unit": "mg/dL" }
  ]
}
```

`lines` yok / `null` / `[]` → satırsız kayıt (eski davranış).

### `PUT /lab-results/{id}`

`UpdateLabResultBody`'ye isteğe bağlı `lines` eklenir (reçete satırlarıyla aynı kısmi yorum):

| `lines` | Anlam |
|---|---|
| alan yok / `null` | **Dokunma**: mevcut satırlar korunur (eski istemci satırları silmez). |
| `[]` | Tüm satırları sil (kayıtta metin sonuç yoksa ve kayıt sonuçlanmışsa 400). |
| dolu dizi | Satır listesi bu liste olur; sıra dizideki sıradır. Satır kimlikleri her yazımda yeniden üretilir. |

Kesinleşmiş muayene kilidi değişmedi: satırlar sonuç alanıdır, kesinleşmiş muayenede de değiştirilebilir. İptal edilmiş kayıtta 409 `LabResults.AlreadyCancelled`.

## 4) Okuma

### `GET /lab-results/{id}` — `LabResultDetailDto`

`lines` eklenir (satır yoksa `[]`), `sortOrder` sırasıyla:

```json
"lines": [
  {
    "id": "guid", "sortOrder": 1, "parameterName": "Glikoz", "value": 140.5, "unit": "mg/dL",
    "referenceLow": 70, "referenceHigh": 120, "flag": "High",
    "previous": { "labResultId": "guid", "resultDateUtc": "datetime", "value": 110, "flag": "Normal" },
    "trend": "Up"
  }
]
```

- `flag`: `"Low" | "Normal" | "High" | null`.
- `previous`: bir önceki karşılaştırılabilir değer (§1); yoksa `null`. `previous.flag` o satırın kendi aralığına göre işaretidir.
- `trend`: önceki değere göre `"Up" | "Down" | "Unchanged"`; `previous` yoksa `null`. Yön, iyi/kötü yorumu taşımaz.

Liste ucu (`GET /lab-results`), muayene özeti ve zaman çizelgesi **değişmedi** (satır taşımaz).

### `GET /lab-results/parameter-history?petId={guid}&parameter={ad}&clinicId={guid?}` `[YENİ]`

Hastada bir parametrenin sonuçlanmış kayıtlardaki değerleri, **yeniden eskiye** (sonuç tarihi, sonra kayıt anı), en çok 100 kayıt. Parametre adı §1'deki gibi eşlenir; birim süzülmez, her noktada birim döner (istemci farklı birimleri ayrı göstermeli). Liste ucuyla aynı kapsam kuralı: `clinicId` ya da aktif klinik bağlamı yoksa 400 `LabResults.ClinicScopeRequired`; klinik kapsamı `IClinicReadScopeResolver` ile çözülür.

```json
[
  { "labResultId": "guid", "resultDateUtc": "datetime", "testName": "Biyokimya", "parameterName": "Glikoz",
    "value": 140.5, "unit": "mg/dL", "referenceLow": 70, "referenceHigh": 120, "flag": "High" }
]
```

## 5) Değişiklik geçmişi (RECORD-002)

Satır listesi lab kaydının tek alanı gibi izlenir: alan adı **`lines`**, eski ve yeni değer satır başına bir satır metin (`"ALT: 150 U/L · referans 10–100"`). Satırlar silinip yeniden yazılsa da metin aynıysa değişiklik yazılmaz.

## 6) Hata kodları

| Durum | Kod | HTTP |
|---|---|---|
| Satırda ad/değer eksik, sayı kuralı, ters aralık, aynı parametre iki kez, 60'tan fazla satır, satır var tarih yok, son sonucu silme | `LabResults.Validation` (doğrulayıcı hatası ise standart doğrulama zarfı) | 400 |
| Parametre geçmişinde klinik kapsamı yok | `LabResults.ClinicScopeRequired` | 400 |
| Diğerleri | `LAB_RESULT_STATUS_API_CONTRACT.md`, değişmedi | — |

## 7) Veri modeli ve migration

Yeni tablo `LabResultLines`: `Id`, `LabResultId` (FK → `LabResults`, cascade), `SortOrder`, `ParameterName` (100), `ParameterKey` (100, eşleme anahtarı), `Value` decimal(18,4), `Unit` (30)?, `ReferenceLow`/`ReferenceHigh` decimal(18,4)?. İndeks `(LabResultId, ParameterKey)`. İşaret saklanmaz, okumada hesaplanır. Migration `AddLabResultLines`; mevcut kayıtlar satırsız kalır, veri dönüşümü yoktur. E-vet aktarımıyla gelen lab geçmişi metin kalır (ADR-048).

## 8) Kullanım etkisi (panel)

- Lab formu isteğe bağlı satır tablosu gönderir; düzenlemede satırlara dokunulmadıysa `lines` gönderilmemesi eski satırları korur.
- Satır varken metin sonuç zorunlu değildir; yalnız satır + tarih kaydı sonuçlandırır.
- Detayda işaret ve eğilim gösterilirken "olası teşhis", yüzde ya da yorum metni kullanılmaz.
