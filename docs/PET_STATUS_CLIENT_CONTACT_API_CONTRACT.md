# Hayvan durumu, müşteri iletişim tercihi ve müşteri bazlı ücret önerisi — API sözleşmesi (ADR-020 S5)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-020-clinic-staff-centered-patient-card.md` (karar 6: müşteri kartı; hayvanlar durum süzgeciyle, iletişim tercihi, ücret bekleyenler). İlgili: `CHARGE_SUGGESTION_API_CONTRACT.md`, `VISITS_API_CONTRACT.md`, `PET_ALERTS_API_CONTRACT.md`. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feat/hayvan-durumu-iletisim`. Migration `20261014162031_AddPetStatusAndClientContactPreferences` üretildi, **hiçbir paylaşılan/geliştirme veritabanına uygulanmadı**. **Yeni izin yok**, seed gerekmez.

---

## 1) Hayvan durumu

### Kavram

- `status`: `Active` (varsayılan; **mevcut tüm kayıtlar**) | `Deceased`.
- `deceasedOn`: vefat tarihi, **opsiyonel** (`yyyy-MM-dd`, İstanbul takvim günü). Yalnız `Deceased` iken dolu olabilir; bugünden ileri ve doğum tarihinden önce olamaz.
- Vefat eden hayvanın **geçmişi okunur ve düzenlenebilir kalır** (muayene güncelleme/kesinleştirme, ek not, tedavi, uygulama, ücret, ödeme engellenmez). Yalnız **yeni bakım kaydı açılmaz** (aşağıda).
- Geri alma: yanlış girildiyse `Active`'e dönülür; vefat tarihi temizlenir, hayvan bütün listelere ve hatırlatmalara geri girer.

### `PUT /api/v1/pets/{id}/status`

İzin: `Pets.Update` (hayvan kaydını düzenleyen herkes; varsayılan rollerin hepsi). Audit: `Pet.MarkDeceased` / `Pet.MarkActive`.

```json
{ "status": "Deceased", "deceasedOn": "2026-10-14", "rowVersion": "AAAAAAAAB9E=" }
```

- `status` zorunlu (`"Active"` | `"Deceased"`; sayı `1`/`2` de kabul edilir).
- `rowVersion` zorunlu: `GET /pets/{id}` yanıtındaki değer. Kayıt arada değiştiyse 409.
- Zaten `Deceased` iken tekrar `Deceased` göndermek tarihi düzeltir.

Yanıt **200**:

```json
{ "id": "guid", "status": "Deceased", "deceasedOn": "2026-10-14", "rowVersion": "AAAAAAAAB9I=" }
```

| Kod | HTTP | Durum |
|---|---|---|
| `Pets.Validation` | 400 | `rowVersion` yok/bozuk; tarih gelecekte veya doğumdan önce; `Active` ile tarih verildi; geçersiz `status` |
| `Pets.NotFound` | 404 | Hayvan yok / başka kiracı |
| `Pets.ConcurrencyConflict` | 409 | Kayıt başka biri tarafından değiştirildi; `GET /pets/{id}` ile yenile |
| `Tenants.TenantInactive` | 403 | Pasif kiracı |

### Okuma

- `GET /api/v1/pets/{id}` (`PetDetailDto`): eklenen alanlar `status` (string), `deceasedOn` (`yyyy-MM-dd` \| null), `rowVersion` (string, Base64).
- `GET /api/v1/pets` (`PetListItemDto`): eklenen alan `status`. Yeni süzgeç **`status=Active|Deceased`**; **verilmezse tümü** (önceki davranış değişmedi). Diğer süzgeçlerle (`clientId`, `speciesId`, `search`) AND.
- `PUT /api/v1/pets/{id}` (mevcut tam güncelleme) **durumu değiştirmez**; durum yalnız bu uçla değişir. Kayıt okunduktan sonra eşzamanlı değiştiyse PUT artık 500 yerine **409 `Pets.ConcurrencyConflict`** döner.

### Vefat eden hayvanın etkileri (kural tek yerde: `Pet.ReceivesCare` / `Pet.EnsureCanStartCare`)

| Yüzey | Davranış |
|---|---|
| `POST /appointments` | **409 `Pets.Deceased`** |
| `POST /visits` (randevulu veya randevusuz geliş) | **409 `Pets.Deceased`** |
| `POST /examinations` | **409 `Pets.Deceased`** |
| Otomatik aşı ve randevu e-posta hatırlatması | Aday değil; gönderilmez, günlük (dispatch log) yazılmaz |
| `GET /vaccinations?onlyOverdue=true` (gecikmiş aşı listesi) | Vefat eden hayvanın aşısı çıkmaz |
| `GET /vaccinations?onlyReceivingCare=true` (pano yaklaşan aşılar; opsiyonel süzgeç) | Vefat eden hayvanın aşısı çıkmaz. Süzgeçsiz genel liste değişmedi (tümü görünür) |
| Pano operasyon uyarıları: `overdueVaccinationsCount`, `upcomingVaccinationsNext7DaysCount` | Sayılmaz |
| Pano `overdueScheduledAppointmentsCount` (saati geçmiş planlı randevu) | **Sayılmaz** (K12). Randevu açık kalır (K4); yalnız gecikmiş iş sayısına girmez |
| Hasta özeti `GET /pets/{id}/summary` | Yanıta `status` eklendi; `vaccinations.nextDue` görünür ama `isOverdue` hep `false` |
| Hasta açık işleri `GET /pets/{id}/open-items` | `vaccinations` bölümü `Empty` (gecikmiş/yaklaşan aşı kalemi yok); sonraki randevu görünür (K4). Bkz. `PET_OPEN_ITEMS_API_CONTRACT.md` |
| Var olan planlı randevu | **Otomatik iptal edilmez** (karar K4); hatırlatması gitmez, gerekirse elle iptal edilir |
| Geçmiş kayıtlar, zaman çizelgesi, hasta özeti, arama | Değişmez, görünür |

---

## 2) Müşteri iletişim tercihi

Mevcut `Client`'ta iletişim tercihi, onay veya hatırlatma izni alanı **yoktu** (kontrol edildi); iki alan eklendi:

| Alan | Tip | Varsayılan | Anlam |
|---|---|---|---|
| `preferredContactChannel` | string: `None` \| `Phone` \| `Email` \| `Sms` \| `WhatsApp` | `None` | Müşterinin tercih ettiği kanal; **yalnız bilgi** (otomatik gönderim kanalını değiştirmez) |
| `remindersOptedOut` | bool | `false` | `true` ise otomatik aşı/randevu hatırlatma e-postaları **gönderilmez** (aday sorgusu dışlar, günlük yazılmaz) |

- `POST /api/v1/clients`: iki alan opsiyonel; verilmezse `None` / `false`.
- `PUT /api/v1/clients/{id}`: iki alan opsiyonel; **`null` (veya alan yok) = dokunma** — alanı bilmeyen eski istemci tercihi silmez (hayvan uyarılarıyla aynı desen). Kanalı temizlemek için `"None"`, bayrağı kaldırmak için `false` gönderilir.
- `GET /api/v1/clients/{id}` (`ClientDetailDto`): iki alan her zaman döner.
- Geçersiz kanal → 400 (`Validation` / model doğrulama).
- Liste (`GET /clients`) ve hızlı kayıt (`POST /clients/quick-register`) **değişmedi**.
- **KVKK:** Bu alanlar onay metni veya rıza kaydı **değildir**; yalnız müşterinin beyan ettiği tercih ve otomatik hatırlatmadan dışlamadır. İmzalı KVKK formu sonucu ayrı alanlarda tutulur; **Ret işaretlenince bu bayrak `true` olur**, Kabul/Yok bayrağı kapatmaz (ADR-025 madde 4): bkz. `KVKK_CONSENT_API_CONTRACT.md` K7.

---

## 3) Müşteri bazlı ücret bekleyen kalemler

`GET /api/v1/charge-suggestions` için yeni opsiyonel süzgeç **`clientId`** (bkz. `CHARGE_SUGGESTION_API_CONTRACT.md` §2):

```
GET /api/v1/charge-suggestions?clinicId={clinicId}&clientId={clientId}&pageSize=200
```

- Müşterinin **bugünkü** bütün hayvanlarının bekleyen (`Pending`) kalemleri tek istekte döner (hayvan başına N istek gerekmez). Kalemin müşterisi hayvanın güncel sahibi olduğundan süzgeç hayvan sahipliği üzerinden uygulanır (`ChargeSuggestionDto.clientId` ile aynı kural).
- **Bekleyen sayı:** yanıttaki `totalItems`. **Tutar:** ayrı toplam alanı yok; istemci sayfadaki kalemlerden `quantity × suggestedUnitPrice` toplar (`suggestedUnitPrice = null` kalem "fiyat bekliyor" sayılır, tutara katılmaz).
- Klinik kapsamı zorunluluğu, izin (`Charges.Read`), sıralama ve diğer süzgeçler **değişmedi**; `clientId` diğer süzgeçlerle AND. Hayvanı olmayan veya başka kiracının müşterisi boş sayfa döner.

---

## 4) Kararlar

| # | Konu | Karar | Gerekçe |
|---|---|---|---|
| K1 | Durum değerleri | Yalnız `Active` / `Deceased`. "Kayıp", "sahiplendirildi", "pasif/arşiv" yok. | ADR-020 yalnız vefatı ister; rakiplerde (E-vet "Öldü" işareti, DaySmart/ezyVet "Deceased") en yaygın ve iş akışını etkileyen durum budur. Sahip değişikliği zaten ayrı kayıttır. Değer eklemek geriye uyumludur (int kolon). |
| K2 | İzin | Mevcut **`Pets.Update`**; yeni izin yok. | Durum hayvan kaydının bir alanıdır; aynı kaydı düzenleyen kişi (resepsiyon dahil, ötanazi/haber resepsiyona da gelir) işaretleyebilmeli. Ayrı izin aynı rollere giden ikinci anahtar olurdu; geri alınabilir ve audit'li olduğu için risk düşük. |
| K3 | Ayrı uç | Durum `PUT /pets/{id}/status` ile değişir; tam güncelleme PUT'u durumu taşımaz. RowVersion zorunlu. | Vefat işaretlemesi bilinçli, tek amaçlı bir eylemdir (onay diyaloğu); eski istemcinin tam PUT'u durumu sessizce geri almasın. Eşzamanlı iki işaretlemede kayıp güncelleme olmasın. |
| K4 | Var olan randevular | **Otomatik iptal edilmez**; hatırlatması gitmez, yeni geliş/muayene açılamaz. | Otomatik iptal randevu durum makinesine ve olaylarına yan etki ekler, geri almada geri getirilemez. En sade ve geri alınabilir seçenek: kapı + hatırlatma dışlaması; frontend durum değişikliğinde açık randevu varsa uyarabilir. |
| K5 | Kapının kapsamı | Yalnız **yeni** randevu, geliş, muayene. Mevcut muayenenin güncellenmesi/kesinleştirilmesi, tedavi, uygulama, aşı kaydı, ücret, ödeme engellenmez. | Ötanazi ve vefat günü kayıtları (işlem, ilaç, ücret) vefat işaretinden sonra da tamamlanabilmeli. |
| K6 | Vefat tarihi | Opsiyonel `DateOnly`; gelecekte ve doğumdan önce olamaz; "bugün" İstanbul günü. | Tarih çoğu zaman bilinir ama zorunlu kılmak veri girişini durdurur. |
| K7 | Dışlama noktası | Hatırlatma dışlaması **aday sorgusunun içinde** (alt sorgu), sonradan değil. | Dışlanan kayıtlar tur sınırını (`batchSize`) tüketmez ve günlük yazmaz; hatırlatma açlığı düzeltmesi korunur. Durum geri alınırsa kayıt yeniden aday olur. |
| K8 | İletişim kanalı | Değerler `Phone`, `Email`, `Sms`, `WhatsApp`; `None` = tercih yok. Yalnız bilgi; gönderim kanalını değiştirmez. | Türkiye'de klinik iletişimi ağırlıkla telefon ve WhatsApp; SMS/WhatsApp gönderimi henüz yok. Nullable yerine `None` değeri: PUT'ta "dokunma (null)" ile "temizle (None)" ayrımı tek alanla yapılabiliyor. |
| K9 | Hatırlatma istemiyor | Tek bayrak, tüm otomatik hatırlatmaları (aşı + randevu) kapsar; tür bazında ayrım yok. | En sade model; tür bazlı tercih ve kanal bazlı rıza ihtiyacı pilotta görülürse eklenir. |
| K10 | Ücret önerisi | Yeni uç değil, mevcut listeye `clientId` süzgeci; toplam alanı yok. | Sözleşme bozulmaz; sayı `totalItems`'ta, tutar istemcide sayfadan hesaplanır (fiyatsız kalem tutara girmez, ayrı gösterilmeli). |
| K11 | Query DB okuma modeli | Durum okuma modeline eklenmedi; `status` süzgeci verilirse liste komut DB'den okunur, okuma modeli yolunda sayfadaki satırların durumu komut DB'den doldurulur. | Okuma modeli bayrağı her ortamda kapalı; Query DB migration'ı bu dilimin kapsamı dışında. |
| K12 | Gecikmiş randevu sayısı | Pano `overdueScheduledAppointmentsCount` vefat edenin açık randevusunu **saymaz**; randevu iptal edilmez, listede/takvimde ve hasta açık işlerinde görünür kalır. `upcomingAppointmentsNext24HoursCount` ve günlük randevu sayıları **değişmedi** (randevu hâlâ takvimdedir). | K4 ile çelişmez: durum makinesine dokunulmaz, yalnız "gecikmiş iş" göstergesi vefat edeni dışlar (aşı sayımlarıyla aynı kural, `Pet.ReceivesCare`). Geri alınınca yeniden sayılır. |

---

## 5) Frontend için değişenler

1. **Hasta kartı:** başlıkta `status = Deceased` rozeti (ve varsa `deceasedOn`); eylem çubuğunda Geldi / Muayene başlat / Randevu al vefat eden hayvanda gizli veya pasif (sunucu da 409 `Pets.Deceased` döner).
2. **Vefat işaretle / geri al:** `PUT /pets/{id}/status` (onay diyaloğu, opsiyonel tarih), `rowVersion` `GET /pets/{id}`'den; 409 `Pets.ConcurrencyConflict` → detayı yenile. Yanıttaki yeni `rowVersion` sonraki yazımda kullanılır.
3. **Müşteri kartı hayvan listesi:** `GET /pets?clientId=…&status=Active` (varsayılan sekme) / `status=Deceased`; süzgeçsiz çağrı tümünü döndürür ve satırda `status` vardır.
4. **İletişim tercihi:** müşteri formunda kanal seçimi ve "Hatırlatma istemiyor" anahtarı; kartta okuma. PUT'ta alanlar her zaman gönderilebilir; temizlemek için `"None"` / `false`.
5. **Ücret bekleyenler:** müşteri kartında tek istek `GET /charge-suggestions?clinicId=…&clientId=…&pageSize=200`; sayı `totalItems`, tutar sayfadan.
6. Yeni hata kodları: `Pets.Deceased` (409), `Pets.ConcurrencyConflict` (409).

---

## 6) Bilinen eksikler ve açık sorular

- **İçe aktarma hatırlatma etkisi** (`reminderImpact`, `IMPORT_API_CONTRACT.md` §6.3) vefat/hatırlatma istemeyen ayrımını yapmaz; tahmin olarak biraz fazla sayabilir (gönderim doğru dışlanır).
- ~~Hasta özeti `isOverdue` vefat edende de hesaplanıyordu~~ ve ~~pano gecikmiş randevu sayısı vefat edeni sayıyordu~~: kapandı (dal `feat/hasta-acik-isler`; özet `status` alanı eklendi, K12).
- Pano `upcomingAppointmentsNext24HoursCount` vefat edenin ileri tarihli açık randevusunu hâlâ sayar (randevu takvimde olduğu için bilinçli; K12).
- **KVKK:** pazarlama/hatırlatma için açık rıza kaydı (metin sürümü, tarih, kanal, kanıt) gerekirse ayrı model; KVKK uzmanı görüşü doğrulanmadı.
- Gerçek veri hacminde sorgu ölçümü yapılmadı (vefat eden hayvan kimlik listesi gecikmiş aşı sayımlarında `NOT IN` olarak kullanılır; kiracı başına küçük olması beklenir).
