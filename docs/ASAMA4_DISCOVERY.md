# Aşama 4 kod keşfi — hasta özeti, timeline, klinik revizyon

Salt okunur keşif (kod yazılmadı, migration yok, test çalıştırılmadı). Taban: yerel `main` `8e4b0f8`. Kaynaklar: ana plan §9, ADR-005/006/009/011, TIMELINE-001..004, EXAM-009/015, RECORD-002. Doğrulanmayan her şey "çıkarım" diye işaretli.

## 1. Bugün ne var

`GET /pets/{id}` (`Pets.Read`): kimlik, sahip adı/telefon/e-posta, `Weight` (tarihsiz), `Notes`, `petAlerts{flags,note}` (ADR-011). Sayfalama yok.

`GET /pets/{id}/history-summary` (`Pets.Read`; `GetPetHistorySummaryQueryHandler`): 7 ayrı blok, her biri **sabit son 10** (`PetHistorySummaryConstants.RecentItemsTake`), sayfalama/imleç/filtre yok, tek istek içinde 7 ayrı sorgu + klinik adı çözümü. Sıralama: olay tarihi azalan, sonra `Id` azalan.

| Tür | Özette var mı | Olay tarihi | Kayıt güncelleme tarihi | Kaynağa bağ |
|---|---|---|---|---|
| Randevu | evet | `ScheduledAtUtc` | **entity'de yok** | yalnız `Id` |
| Muayene | evet (yalnız `VisitReason`) | `ExaminedAtUtc` (kullanıcı düzenleyebilir) | `UpdatedAtUtc` var, DTO'ya konmamış | `Id` |
| Tedavi | evet | `TreatmentDateUtc` | `UpdatedAtUtc` var | `ExaminationId` |
| Reçete | evet | `PrescribedAtUtc` | `UpdatedAtUtc` var | `ExaminationId`, `TreatmentId` |
| Lab | evet | `ResultDateUtc` | `UpdatedAtUtc` var | `ExaminationId` |
| Yatış | evet (+`IsActive`) | `AdmittedAtUtc` (taburcu: `DischargedAtUtc`) | `UpdatedAtUtc` var | `ExaminationId` yok DTO'da |
| Ödeme | evet | `PaidAtUtc` | **entity'de yok** | `Id` |
| Aşı | **hayır** | `AppliedAtUtc` / `DueAtUtc` (nullable, duruma bağlı) | `UpdatedAtUtc` var | — |
| Visit (geliş) | **hayır** | `ArrivedAtUtc` | — | — |

Bulgular:
- **Klinik kapsam:** `IClinicReadScopeResolver` + `DashboardSpecificationClinicScope` ile (aktif klinik veya erişilebilir klinikler). Hayvan/müşteri tenant geneli.
- **İzin açığı (çıkarım, rollerde şu an tetiklenmiyor):** uç yalnız `Pets.Read` ister ama ödeme/lab/reçete/tedavi/yatış içeriği döner. Rollerde (`RolePermissionBindings`) `Pets.Read` olan herkes diğer `*.Read` izinlerine de sahip; özel rol `Pets.Read`-only olursa klinik ve ödeme verisi sızar. Aşama 4'te tür bazlı izin süzmesi şart.
- Özet tek DTO'da "boş liste" döner; "kayıt yok" ile "yetki yok / okunamadı" ayrımı yok (§3).
- `Appointment` ve `Payment` entity'lerinde `CreatedAtUtc/UpdatedAtUtc` bulunamadı (grep boş). Bunlar için "kayıt güncelleme tarihi" yok.
- Query DB projeksiyonu yalnız Appointments/Clients/Payments/Pets; `QueryReadModels` bayrakları appsettings'te kapalı. Muayene/tedavi/reçete/lab/yatış/aşı/visit için read model yok.

## 2. Birleşik timeline — en küçük temiz tasarım

**Önerilen:** `GET /pets/{id}/timeline` — ayrı tablo/kopya yok, tek generic entity yok. Handler her kaynak için mevcut spec desenini kullanarak **kaynak başına keyset sorgu** çalıştırıp bellekte birleştirir (k-yollu merge).

| Konu | Öneri |
|---|---|
| Okuma modeli | Salt okunur `PetTimelineItemDto { type, sourceId, eventAtUtc, clinicId, clinicName, title, summary?, status?, linkRef{type,id}, relatedExaminationId? }`. Tür başına küçük bir "kaynak okuyucu" (mevcut `*ForPetRecentSpec`'in imleçli genişlemesi); ortak arayüz: `(petId, scope, cursor, take+1) → rows`. |
| Sıralama | `(eventAtUtc DESC, typeOrder DESC, sourceId DESC)` — tam sıralı, eşit tarihte bile deterministik. |
| İmleç | Opak base64 `(eventAtUtc, typeOrder, sourceId)`; her kaynakta `WHERE (date,id) < cursor ORDER BY … TAKE limit+1`, birleştir, `limit` al, kalan varsa `nextCursor`. `PageRequest` (offset/`TotalItems`) **kullanılmaz**: kayıt eklenince sayfa kayar; toplam sayım 8 kaynak için pahalı ve gereksiz. |
| Kayma riski | Olay tarihi düzenlenebilir (`ExaminedAtUtc`, `TreatmentDateUtc`…): kayıt sayfalar arası kayabilir. Kabul edilebilir; sözleşmede yazılır. Silme/yazma sırasında imleç bozulmaz (değer tabanlı). |
| Olay tarihi vs güncelleme | Sıralama yalnız olay tarihi. `updatedAtUtc` ayrı alan (varsa); randevu/ödemede `null`. |
| Kaynak bağlantısı | `linkRef{type,id}` + frontend rota eşlemesi; backend URL üretmez. Muayeneye bağlı alt kayıtlar `relatedExaminationId` ile (filtre/gruplama istemcide). |
| Filtre | `types` (çoklu), `fromUtc/toUtc` (olay tarihi), `clinicId` (mevcut scope kuralıyla). Metin araması ilk dilimde yok. |
| Yetki | Tür başına `*.Read` izni; izni olmayan türler sorguya girmez ve yanıtta `omittedTypes: ["Payment",…]` döner → "yetki yok" ile "kayıt yok" ayrılır. Klinik scope history-summary ile aynı çözümleyici. |
| DB | **Command DB** (read model yok, bayraklar kapalı; Bugün ve history-summary de böyle). Query DB'ye taşıma ayrı iş. |
| Performans | Her tabloda `(TenantId, PetId)` indeksi var (Exam/Treatment/Rx/Lab/Hosp/Vacc/Payment/Appt/Visit). `(TenantId, PetId, Tarih)` bileşik indeks **yok**: hayvan başına satır az olduğu için ilk sürümde sorun beklenmez (çıkarım; ölçülmedi). Sayfa başına en çok 8×(limit+1) satır, `limit` üst sınırı (ör. 50). Ölçülü sorun çıkarsa hedefli indeks migration'ı ayrı onayla. |
| Özet blokları | `history-summary` uyumluluk için kalır; timeline yeni uç. Frontend geçince özet ucu ya "son 5" alt kümesine inebilir ya da kaldırılır (karar §6). |
| Aşı olayı | `Applied` → `AppliedAtUtc`; `Scheduled` → ileri tarih, timeline'a değil "yaklaşan"a (§3); `Cancelled` → `DueAtUtc ?? CreatedAtUtc` (ürün kararı D4). |
| Randevu+Visit çift satırı | Aynı gelişin randevusu ve Visit'i tek satır olmalı (ADR-009 gibi): Visit varsa randevu satırı onun içine katlanır (karar D4). |

## 3. Hasta özeti — veri hazırlığı

| Parça | Durum |
|---|---|
| Kimlik/sahip | Hazır (`PetDetailDto`). |
| Uyarılar | Hazır (`petAlerts`, ADR-011, `Pets.Read`). "Uyarı yok" = `flags: []`. |
| Aktif yatış | Veri var: `DischargedAtUtc == null`; klinik başına hayvan başına filtreli benzersiz indeks (`Hospitalization` config). `GET /hospitalizations?petId&activeOnly` var. History-summary'deki `IsActive` yalnız son 10'un içinde; **özet için ayrı sorgu** gerekli (ADR-§9.4). |
| Açık işler | **Yok** (lab/tedavi/reçetede açık-kapalı durum yok; kuyruktaki karar: ilk sürümde göstergesiz). |
| Kilo | `Pet.Weight` tarihsiz → tarih uydurulmaz (karar 3). **Tarihli kaynak zaten var:** `Examination.WeightKg` + `VitalsMeasuredAtUtc` (yalnız muayene vitali). Özet "son ölçüm: X kg, tarih, kaynak=Muayene" gösterebilir; `Pet.Weight` ayrı "profil kilosu (tarihsiz)" olarak. Ayrı ölçüm tablosu gerekmez (karar D2). |
| Aşı | Veri hazır ama özette yok: yapılmış = `Status=Applied` en yeni `AppliedAtUtc`; yaklaşan = `Status=Scheduled` en yakın `DueAtUtc` (Scheduled'da `DueAtUtc` geleceğe zorunlu, sonra gecikmiş sayılır); `onlyOverdue` filtresi mevcut. İkisi ayrı alan. |
| Son ziyaret | Visit/muayene en yeni kayıt (hazır). |

**Üçlü durum ("uyarı yok / veri alınamadı / yetki yok")** için sözleşme: özet DTO'sunda her bölüm `{ state: "ok"|"empty"|"forbidden"|"unavailable", data }`. `forbidden`: kullanıcıda ilgili `*.Read` yok (uyarılar `Pets.Read` altında, ayrı yetki yok → "yetki yok" yalnız diğer bölümlerde anlamlı). `unavailable`: bölüm sorgusu hata verdi — **bu bugün ifade edilemiyor**; bölüm bazlı try/catch ve kısmi yanıt gerektirir (mevcut `Result` deseni tek hata döner). Alternatif: özeti tek istek yerine bölüm bazlı uçlara bölmek (istemci hata durumunu kendi görür) — D3.

## 4. Klinik revizyon / kesinleştirme

Mevcut `Examination`: `RowVersion` (SQL rowversion, PUT'ta zorunlu, 409), `CreatedAtUtc`, `UpdatedAtUtc`, kısmi PUT (`UpdateClinicalContent`). **Durum/Draft-Finalized yok; revizyon/addendum yok; "kim güncelledi" alanı yok** (`CreatedBy/UpdatedBy` bulunamadı). `Examinations.Update` yetkisi: Admin, Owner, ClinicAdmin, Veteriner; Sekreter yalnız Read. Muayene komutları audit'e bağlı değil (`IAuditableRequest` uygulanmamış; ADR-009 notu). Genel `AuditLog` istek yükünü JSON olarak saklar (maskeleme yalnız parola/token anahtarlarında): klinik metni oraya yazmak hem erişim kontrolü hem PII bakımından klinik geçmişin yerini tutmaz (ADR §9.8 ile uyumlu).

En küçük temiz model (öneri):

| Parça | Öneri |
|---|---|
| Durum | `Examination.Status` (`Draft`/`Finalized`) + `FinalizedAtUtc`, `FinalizedByUserId`. Mevcut kayıtlar `Draft` başlar (davranış değişmez). Kural `Examination` domain'inde: `Finalized` iken `UpdateClinicalContent` reddedilir (`Examinations.Finalized`), yalnız `Finalize()` ve `AddAddendum()` açıktır. Sessiz değişiklik korumasını hem domain hem handler (RowVersion zaten var) verir. |
| Addendum | `ExaminationAddendum` (Id, ExaminationId, TenantId, ClinicId, `Text`, `Reason` zorunlu, `CreatedAtUtc`, `CreatedByUserId`) — append-only, güncelleme/silme yok. |
| Revizyon saklama | **Kesinleştirilmiş içeriğin ilk hali değişmez** olduğundan (düzeltme addendum ile), ayrı "önceki sürüm kopyası" tablosu ilk dilimde gereksiz. Taslak aşamasındaki düzenlemelerin geçmişi tutulmaz (karar D1). Eğer kesinleştirme geri alınabilirse (reopen) o an `ExaminationRevision` anlık görüntüsü gerekir → D1'e bağlı. |
| Yetki | Yeni izinler: `Examinations.Finalize` (öneri: Veteriner + ClinicAdmin/Admin/Owner), `Examinations.Addendum` (aynı). Okuma: addendum içeriği `Examinations.Read` ile; "yetkisiz kişi revizyon içeriğini okuyamaz" kabulü için revizyon/addendum ayrı `Examinations.ReadRevisions`i gerektirebilir (D1). Yeni izin = seeder + özel rollere elle ekleme (deploy notu; `Visits.*` ile aynı). |
| Ziyaret ilişkisi | Ayrı kavramlar: `Finalize` Visit durumunu değiştirmez; `Visit.Complete` muayeneyi kesinleştirmez. Muayene oluşturma→randevu `Completed` otomatik davranışı (ADR-009 K2) aynen kalır. Opsiyonel uyarı: Visit tamamlanırken bağlı muayene `Draft` ise istemcide uyarı (zorunluluk değil, D1). |
| Etkilenenler | Muayene PUT (409 yanında yeni 409/422), `ExaminationDetailDto`/liste/`related-summary`, export/rapor okuyucuları (alan eklemesi geriye uyumlu), frontend muayene ekranı (salt okunur mod + addendum formu). Toplu geçiş: eski kayıtlar `Draft`; "hepsini kesinleştir" yapılmaz. |
| Migration | Alan ekleme + 1 yeni tablo; yalnız yerel DB, paylaşılan/üretim ayrı onay. |

## 5. Önerilen dilimler (sıra, bağımlılık, test odağı)

| # | Dilim | Bağımlılık | Test odağı | Frontend ile paralel? |
|---|---|---|---|---|
| A | Hasta özeti ucu: aktif yatış (ayrı sorgu), son/yaklaşan aşı, son muayene vitali (kilo+tarih+kaynak), bölüm durumu (`ok/empty/forbidden`) | D2, D3 | Handler birimleri (izin yok → forbidden; Yatış 11 kayıt içinde aktif olan bulunur; Scheduled/Applied ayrımı); klinik scope | Sözleşme dokümanı yazılınca frontend özet kartıyla paralel |
| B | Timeline ucu (imleçli, `types/from/to`, `omittedTypes`) | D4 | Merge sırası ve eşit tarih ayrımı; imleç ardışık sayfalarda kayıp/çift yok; izin süzmesi; tenant/klinik sızıntısı; limit üst sınırı. Entegrasyon (LocalDB) 1 senaryo: çok kaynaklı hasta | Sözleşme + sahte yanıtla frontend akışı paralel |
| C | Derin bağlantı verisi + `relatedExaminationId`, history-summary'ye `updatedAtUtc`/`ExaminationId` ekleri (geriye uyumlu) | B | DTO alanları | Evet |
| D | Kesinleştirme: `Status`, `Finalize`, donmuş içerik koruması, `Examinations.Finalize` | D1, D5 | Domain: Finalized'da güncelleme reddi, kısmi PUT/ClearVitals dahil; RowVersion yarışı; eski kayıt Draft; izin | Backend önce (frontend yalnız sözleşmeden sonra) |
| E | Addendum (append-only, gerekçe zorunlu) + okuma yetkisi | D | Zorunlu gerekçe; güncelleme/silme yolu yok; kim/ne zaman; yetkisiz okuma 403 | Sözleşme sonrası paralel |
| F | Uyarı sertleştirme: history-summary'de tür bazlı izin süzmesi (B ile aynı yardımcı) | B | `Pets.Read`-only rolde yalnız izinli bloklar | — |

Sıra: **A → B → C** (okuma tarafı, migration yok) ve **D → E** (yazma tarafı, migration). Okuma ve yazma grupları birbirinden bağımsızdır; ayrı dallarda paralel yürüyebilir. F, B'nin yardımcısını kullanır. Aşama 3 (ücret) ile çakışma: Timeline'daki ödeme türü `Payment` okuma modeline bağlı; Aşama 3 ödeme/`Charge` şeklini değiştirirse B'de ödeme kaynağı sonra bağlanır (kaynak-okuyucu arayüzü sayesinde izole). Ödeme olayını ilk dilimde `omittedTypes` içinde bırakma seçeneği D4.

## 6. Ürün kararı gerekenler (kullanıcıdan)

| # | Soru | Seçenekler | Öneri ve gerekçe |
|---|---|---|---|
| D1 | Kesinleştirme politikası | **a)** Kesinleştirince salt okunur, düzeltme yalnız addendum (geri açma yok). **b)** a + yetkili "yeniden aç" (`ExaminationRevision` anlık görüntüsüyle). **c)** Kesinleştirme yok, yalnız revizyon geçmişi | **a** — ADR §9.7 ile birebir, en küçük model, sessiz değişikliği keser. b ancak sahada "yanlış kesinleştirdim" şikâyeti olursa. Ayrıca: kim kesinleştirir (öneri: Veteriner+yönetim, Sekreter hayır) ve addendum/revizyon içeriğini kim okur (öneri: `Examinations.Read` sahipleri; ayrı izin yalnız istenirse). |
| D2 | Kilo geçmişi | **a)** Muayene vitallerinden türet (tarihli, ek tablo yok). **b)** Ayrı `PetWeightEntry` tablosu (klinik dışı ölçüm, tartı). | **a** — kullanıcıdan yeni veri istemez, kopya yok; `Pet.Weight` tarihsiz "profil kilosu" kalır. b ihtiyaç doğarsa. |
| D3 | "Veri alınamadı" nasıl ifade edilir | **a)** Tek özet ucu, bölüm başına `state` + kısmi yanıt. **b)** Bölüm bazlı ayrı uçlar. | **a** — tek istek (UX hızı), `state` sözleşmede; yalnız hata durumunda kısmi yanıt karmaşıklığı. Şüphede b daha basit ama istek sayısı artar. |
| D4 | Timeline kapsamı (ilk sürüm) | Hangi türler: Randevu+Visit tek satır mı? Ödeme dahil mi (Aşama 3 sürüyor)? İptal edilen aşı/randevu görünsün mü? Yatış tek olay (kabul) mı, kabul+taburcu iki olay mı? | **Öneri:** muayene, tedavi, reçete, lab, aşı (uygulanan), yatış (tek olay, taburcu alanı), geliş (Visit; randevu katlanır); ödeme Aşama 3 oturunca; iptal/yanlış-işaretli kayıtlar varsayılan gizli, filtreyle açılır. |
| D5 | Mevcut kayıtlar ve kesinleştirme etkisi | Eski muayeneler `Draft` mı kalsın, belirli tarihten eskisi otomatik `Finalized` mı? | **Draft kal** — otomatik kilitleme geri dönüşü zor ve veri değiştirir; "yaşlı taslak" uyarısı ayrı iş. |

## 7. Doğrulanmayanlar

- Hiçbir test çalıştırılmadı; performans ölçülmedi (indeks/boyut çıkarımı).
- Entegrasyon (LocalDB) davranışı, EF sorgu planları, `UNION`'sız merge'in gerçek gecikmesi doğrulanmadı.
- `Appointment`/`Payment` için güncelleme tarihi yokluğu grep ile çıkarım.
- `Examinations.Finalize` gibi izin adları ve rol dağılımı öneridir; mevcut kodda yoktur.
