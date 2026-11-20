# Hesabı kapat (CHECKOUT-001) — API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynaklar:
- Araştırma: ürün deposu `research/p2-arastirma-2026-11-09.md` §1 (seçenek C).
- Onaylı tasarım: FE commit `07e25890` (`src/app/pages/taslak/hesabi-kapat/*`).
- Kuyruk: `roadmap/AJAN-KUYRUGU.md` (CHECKOUT-001).

Hata zarfı standart `ProblemDetails` + `extensions.code` (`BACKEND-CONTRACT-STANDARD.md` §9).

> **Durum:** uygulanıyor (2026-11-20). Dal `feature/hesabi-kapat`. Migration yok, yeni izin yok, yeni hesap kuralı yok.

## 0) Kararlar

| # | Karar | Gerekçe |
|---|---|---|
| K1 | **Yalnız okuma ucu eklenir**: `GET /visits/{id}/checkout-summary`. Yazma mevcut uçlarla yapılır: kalem çevir/ücretsiz say, kredi kullan, ödeme al, gelişi başlat/tamamla. Yeni toplu `checkout` komutu yok. | Araştırma seçenek C. Yazma kuralları tek yerde kalır. Her yazma adımı zaten tekrar denemeye dayanıklıdır (`requestId` ya da durum kontrolü). |
| K2 | Kapanış zamanı/kişisi gelişe ayrıca **yazılmaz** (`Visit.CheckedOutAtUtc` yok, migration yok). Kapanış = geliş Tamamlandı + ödeme durumu (`IVisitOpenItemsReader`). | Araştırma ürün sorusu 1 önerisi; pilotta bakılır. |
| K3 | Müşterinin **başka gelişlerden kalan açık ücretleri gösterilir, seçili gelmez**. | Araştırma ürün sorusu 2 önerisi, tasarım "Eski borçlar seçili gelmez". |
| K4 | **Borçla kapatma serbest**. Geliş, ödeme olmadan da tamamlanır. | ADR-009: bakım ile ödeme ayrıdır. |
| K5 | Bekleyen geliş önce **başlatılır**, sonra tamamlanır. Bunlar iki ayrı mevcut çağrıdır; Bekliyor → Tamamlandı geçişi eklenmez. | Domain kuralı değişmez. |
| K6 | Geliş, **ücretsiz onayıyla** (`withoutCharge: true`) yalnız şu durumda tamamlanır: panelde ücretlenecek bir şey kalmamış, yani bekleyen kalem kalmamış ya da hepsi ücretsiz sayılmış, ve gelişin hiç ücreti yok. Karar bekleyen kalem varsa onaysız tamamlanır; Bugün'de "Ücret yok" rozeti kalır. | Bugün'deki "Ücretsiz tamamla" kuralıyla aynı anlam. |
| K7 | Kapanıştan sonraki **e-Belge önerisi** Bugün'deki ziyaret kapanışıyla aynıdır. Aynı bileşen (`EDocumentIssueDialogComponent`), aynı kaynak (`{ visitId }`) ve aynı izin (`EDocuments.Issue`) kullanılır. Öneri, bağlantı `Connected` ise ve önizlemede grup varsa açılır. | Kuyruk şartı: "aynen çıkmalı". |
| K8 | Parçalı ödeme: her tahsilat satırı **ayrı bir `POST /payments`** isteğidir. Seçili ücretler sırayla satırlara dağıtılır. Fazlası son satırda `creditAmount` olur (FIN-005). | Mevcut ödeme kuralı; tek ödeme tek yöntem. |
| K9 | Krediden ödeme, ödemelerden **önce** `POST /clients/{id}/credits/apply` ile yapılır. | Mevcut kredi kuralı (FIN-005). |
| K10 | Bilgi fişi "ziyaret ve tahsilat özeti"dir: **resmî belge değildir**, satış fişi deseniyle A5 yazdırılır. | Araştırma §1 akış 5. |

## 1) Uç

`GET /api/v1/visits/{id}/checkout-summary`

| | |
|---|---|
| Kimlik | Personel JWT, policy `Visits.Read`. |
| Kapsam | Geliş, `GET /visits/{id}` ile aynı kuralla bulunur. Başka klinikteki ya da atanmamış klinikteki geliş **404** `Visits.NotFound` döner. |
| `charges` bölümü | Yalnız `Charges.Read` varsa dolar; yoksa `null` döner. Bu, Bugün'deki açık iş göstergesiyle aynı kuraldır ve ucun yeni veri açmasını önler. |

## 2) Yanıt (200)

```json
{
  "visitId": "…",
  "clinicId": "…",
  "careStatus": "InProgress",
  "arrivedAtUtc": "2026-11-20T06:40:00Z",
  "completedAtUtc": null,
  "isVoided": false,
  "responsibleVeterinarianName": "Deniz Kaya",
  "arrivalReason": "Yıllık kontrol",
  "petId": "…",
  "petName": "Boncuk",
  "speciesName": "Kedi",
  "clientId": "…",
  "clientName": "Ayşe Yılmaz",
  "clientPhone": "0532 000 45 67",
  "currency": "TRY",
  "charges": {
    "chargeIndicator": "PaymentPending",
    "pendingItems": [
      {
        "id": "…", "sourceType": "Administration", "petId": "…", "petName": "Boncuk",
        "description": "Karma aşı", "quantity": 1, "unit": "doz",
        "suggestedUnitPrice": 850, "occurredAtUtc": "…"
      }
    ],
    "visitCharges": [
      {
        "chargeId": "…", "petName": "Boncuk", "description": "Muayene",
        "amount": 600, "allocatedAmount": 0, "outstandingAmount": 600, "createdAtUtc": "…"
      }
    ],
    "otherOpenCharges": [
      {
        "chargeId": "…", "visitId": "…", "visitArrivedAtUtc": "…", "petName": "Boncuk",
        "description": "Tırnak kesimi", "amount": 150, "allocatedAmount": 0, "outstandingAmount": 150,
        "createdAtUtc": "…"
      }
    ],
    "creditBalance": 0
  }
}
```

| Alan | Kaynak (kural tek yerde) |
|---|---|
| Üst bilgi | `GET /visits/{id}` (`GetVisitByIdQuery`). Hayvan ve tür `PetByIdSpec`'ten gelir. Müşteri hayvanın **güncel sahibidir**, ücretin müşterisiyle aynı kural (ADR-018). |
| `chargeIndicator` | `IVisitOpenItemsReader.GetForVisitsAsync`: `NoChargeableWork` \| `ChargeMissing` \| `PaymentPending` \| `PartiallyPaid` \| `Paid`. Bugün satırındaki rozetle aynıdır. |
| `pendingItems` | `GET /charge-suggestions?visitId=` (`GetChargeSuggestionsQuery`, yalnız `Pending`), gelişin kliniğinde. `suggestedUnitPrice` `null` olabilir; bu durumda fiyatı resepsiyon girer. |
| `visitCharges` | `GET /charges?visitId=` (`GetChargesListQuery`), gelişin kliniğinde. Ödenmiş ücretler de gelir, fiş ve "zaten ödendi" görünümü bunları kullanır. İptal edilen ücretler gelmez. |
| `otherOpenCharges` | `GET /clients/{id}/balance` (`GetClientBalanceQuery`). Yalnız gelişin kliniğindeki, bu gelişe ait olmayan ve kalanı sıfırdan büyük ücretler gelir. Sıra eskiden yeniye. |
| `creditBalance` | Aynı balance sorgusu: açık kredilerin kalanı (FIN-005). |

Liste üst sınırı: `pendingItems` ve `visitCharges` en çok 200 satırdır (liste uçlarının sınırı). Tek gelişte bu sınır aşılmaz.

## 3) Hatalar

| Durum | Kod |
|---|---|
| 404 | `Visits.NotFound` (geliş yok, başka klinikte ya da kapsam dışı) |
| 400 | `Tenants.ContextMissing` |
| 403 | `Visits.Read` yok (policy) |

İptal edilmiş (voided) geliş **200** döner ve `isVoided: true` olur. Panel bu durumda yalnız bilgi gösterir.

## 4) Yazma akışı (istemci; yeni uç yok)

Panel "Kaydet" dediğinde adımlar sırayla çalışır. Bir adım hata verirse panel durur. Kaydedilenler geçerli kalır, kalan tutar yeniden gösterilir.

1. Ücrete çevrilecek her kalem: `POST /charge-suggestions/{id}/convert`. Gövde `{ unitPrice?, requestId }`; `requestId` kalem başına bir kez üretilir, tekrar deneme aynı kimliği kullanır.
2. Ücretsiz sayılacak her kalem: `POST /charge-suggestions/{id}/waive`. Gövde `{ reason }`, 5–500 karakter.
3. Krediden ödeme: `POST /clients/{clientId}/credits/apply`. Gövde `{ clinicId, requestId, allocations[] }`.
4. Her tahsilat satırı: `POST /payments`. Gövde `{ clinicId, clientId, petId, amount, currency, method, paidAtUtc, requestId, allocations[], creditAmount?, cashRegisterId? }`. Satır başına bir `requestId` kullanılır.
5. Geliş Tamamlandı değilse: Bekliyor ise önce `POST /visits/{id}/start`, sonra `POST /visits/{id}/complete` (`{ withoutCharge }`, K6). İkisinin de izni `Visits.Update`.
6. e-Belge önerisi (K7).

Çevrilen kalemlerin yeni ücret kimlikleri `convert` yanıtından alınır ve tahsis listesine bu gelişin ücreti olarak eklenir.

## 5) Testler

- Birim: handler bölüm kuralları (izin yokken `charges` `null`; başka gelişin ücreti `otherOpenCharges`'a gider, bu gelişinki `visitCharges`'a; başka kliniğin ücreti gelmez).
- Entegrasyon: `GET /visits/{id}/checkout-summary` 200 ve tam şekil; izinsiz kullanıcıda `charges` `null`; başka klinik 404.
