# PR Checklist: Contract Impact

Bu checklist, backend/frontend contract etkisi olan PR'larda zorunlu kontrol listesi olarak kullanılır.

## 1) Contract Etkisi

Her madde için: `Evet / Hayır / N.A.` işaretleyin ve gerekiyorsa kısa not ekleyin.

- [ ] Endpoint path değişti mi?
- [ ] HTTP method değişti mi?
- [ ] Request body alanları değişti mi? (ekleme/silme/rename/type/nullability)
- [ ] Response body alanları değişti mi? (ekleme/silme/rename/type/nullability)
- [ ] Enum değişikliği var mı? (yeni değer, kaldırılan değer, temsil biçimi)
- [ ] Validation kuralı değişti mi? (zorunluluk, aralık, format, iş kuralı)
- [ ] ProblemDetails / error code sözleşmesi değişti mi?
- [ ] Tenant/clinic context ownership kuralı değişti mi? (context-first/body-first/mismatch davranışı)

---

## 2) Frontend Impact

- [ ] Etkilenen ekran(lar) listelendi mi?
- [ ] Mapper dönüşümü güncellemesi gerekiyor mu?
- [ ] API model/type güncellemesi gerekiyor mu?
- [ ] Form model veya view model güncellemesi gerekiyor mu?

Kısa etki özeti:
- Etkilenen modüller:
- Etkilenen endpointler:
- Kırıcı değişiklik var mı?:

---

## 3) Smoke Test Checklist

İlgili endpoint/modül için uygulanacak minimum smoke testler:

- [ ] Create akışı
- [ ] List akışı
- [ ] Detail akışı
- [ ] Edit/Update akışı
- [ ] Validation error case
- [ ] Tenant/clinic context case
- [ ] Enum parse case
- [ ] Error parsing case (ProblemDetails + code)

Test notları:
- Test ortamı:
- Test edilen kullanıcı/tenant/clinic:
- Sonuç:

---

## 4) Notlar

Bu bölüm PR açıklamasında kısa ve net doldurulmalıdır.

- Değişen request alanları:
- Değişen response alanları:
- Enum etkisi:
- Validation etkisi:
- Gerekli takip işleri (backend/frontend):

---

## Kullanım Kuralı

- Contract etkisi olan tüm PR’larda bu checklist doldurulmadan merge yapılmamalıdır.
- Kırıcı değişiklik varsa release/deprecate planı PR içinde açıkça belirtilmelidir.
