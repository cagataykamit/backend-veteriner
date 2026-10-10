# Günlük hasta/sahip arama sözleşmesi (SEARCH-001)

Backend tek doğruluk kaynağıdır. Endpoint, istek ve yanıt şekli **değişmedi**; yalnızca `search` parametresinin eşleştirme kuralı genişledi.

| Endpoint | Aranan alanlar |
|----------|----------------|
| `GET /api/v1/clients?search=` | Sahip adı, e-posta, telefon |
| `GET /api/v1/pets?search=` | Hasta adı, tür, ırk (serbest metin + katalog), **mikroçip**, sahip adı/e-posta/telefon |

## Eşleştirme kuralları

1. **Token'lar:** Terim boşluk ve noktalama ile parçalanır (en fazla 5 token). **Her token** en az bir alanda geçmelidir (alt dizi eşleşmesi, büyük/küçük harf duyarsız). Sıra önemsizdir: `yılmaz ayşe` = `Ayşe Yılmaz`; `ayşe.yılmaz` ve `ayşe   yılmaz` aynı sonucu verir.
2. **Türkçe harf:** `ş/ğ/ü/ö/ç` ve aksanlı karşılıkları birbirine eşit sayılır (`sule` → `Şule`). `ı/İ` ile `i/I` eşittir (`kirmizi` → `Kırmızı`, `ipek` → `İpek`, `KIRMIZI` → `Kırmızı`). Bu katlama ad, tür ve ırk alanlarında geçerlidir.
3. **Telefon:** Terim yalnızca rakam ve `+ ( ) - . /` boşluk içeriyorsa tek rakam dizisi sayılır. `0532 123 45 67`, `+90 532 123 45 67`, `(0532) 123-45-67`, `905321234567` ve `532 123` aynı kayda ulaşır (saklanan biçim `905XXXXXXXXX`). Kısmi numara (alt dizi) de eşleşir.
4. **Mikroçip (yalnızca `/pets`):** Aynı rakam kuralı: `985 1210 0123 4567` boşluksuz mikroçip ile eşleşir. Alt dizi eşleşir. Saklanan mikroçip içinde boşluk/tire varsa (kayıt sırasında girilmişse) eşleşmez; kayıtta olduğu gibi aranmalıdır.
5. **Sahip → hasta:** `/pets` aramasında, tüm token'larla eşleşen sahibin bütün hastaları döner.
6. Boş veya yalnızca noktalama olan terim filtre uygulamaz (önceki davranışla aynı: tüm kayıtlar).

## Değişmeyenler

- Tenant filtresi her zaman uygulanır; arama kuralı bu filtreyi değiştirmez. `/clients` ve `/pets` için klinik filtresi yoktur (Client/Pet varlıklarında `ClinicId` yok); bu işte değiştirilmedi.
- Sayfalama, sıralama (`sort`/`order` işlenmez), yanıt alanları ve hata zarfı aynıdır.
- Randevu/muayene/aşı/ödeme listelerindeki `search` bu işte **değişmedi** (eski kural: `%terim%`).

## Kapsam dışı / bilinen kısıtlar

- Query DB read model yolu (`QueryReadModels:ClientsEnabled` / `PetsEnabled`, tüm ortamlarda şu an `false`) eski `%terim%` kuralıyla çalışır; mikroçip read model'de yoktur. Bayrak açılmadan önce ayrıca ele alınmalıdır.
- Mikroçip için indeks yoktur; tablo taraması yapılır (tenant ve sahip `ClientId` ile kapsamlı olduğundan ölçek sorunu beklenmez, ölçülmedi).
- Sunucu arama sonuçlarını alaka sırasına koymaz; sıralama mevcut kuraldadır (ad).
