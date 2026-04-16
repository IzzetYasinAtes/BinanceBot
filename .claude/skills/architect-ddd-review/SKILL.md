---
name: architect-ddd-review
description: DDD (Domain-Driven Design) sınır denetimi. Aggregate boundary, entity vs value object seçimi, domain event adlandırma, ubiquitous language tutarlılığı, anemic model taraması. architect agent'ın mevcut tasarımı DDD açısından denetlediği skill.
---

# architect-ddd-review

Tasarım veya PR gelince DDD açısından çalışır durumda mı — checklist.

## Checklist

### 1. Aggregate Boundary
- [ ] Her aggregate'in tek **root entity**'si var mı?
- [ ] Aggregate dışından sadece root'a erişim var; içindeki entity'lere direkt erişim YOK?
- [ ] Bir transaction birden fazla aggregate'i değiştirmiyor? (eventual consistency domain event ile)
- [ ] Aggregate boyutu makul mi? (büyükse böl: "Order" ile "Shipment" ayrı olabilir)

### 2. Entity vs Value Object
- [ ] Kimliği önemli olan → **Entity** (id ile eşit).
- [ ] Sadece değeri önemli olan → **Value Object** (immutable, tüm field'lar eşitse eşit).
- [ ] Money, Address, DateRange, Price, Symbol — genelde VO olur, Entity değil.
- [ ] VO mutable mı? HAYIR olmalı.

### 3. Domain Events
- [ ] Event adları **geçmiş zaman** mi? (KlineIngested, OrderPlaced, StopLossTriggered)
- [ ] Event'te aggregate id + önemli field'lar var; internal state YOK.
- [ ] Event'ler domain layer'da; handler Application'da.
- [ ] Event publish zamanı: aggregate save sonrası (outbox pattern tercih — özellikle at-least-once).

### 4. Ubiquitous Language
- [ ] Kod (class/method/field) adları iş dilinden mi alınmış? ("Kline" yerine "CandleStick" yazılmışsa inconsistency).
- [ ] `docs/glossary.md` ile tutarlı mı?
- [ ] Aynı konsept farklı yerde farklı isimle mi geçiyor? (merge et)

### 5. Anemic Model
- [ ] Domain class'ı sadece getter/setter'dan mı oluşuyor? Kurallar handler'da mı? (ANEMIC — fix)
- [ ] Invariant'lar constructor'da veya behavior metodlarında korunuyor mu? (`order.AddItem()` gibi)
- [ ] `public set;` ile dışarıdan state değişebiliyor mu? (private set + behavior method)

### 6. Repository
- [ ] Her entity'ye repository var mı? (YANLIŞ — repo-per-aggregate-root)
- [ ] Repository interface Domain'de, implementation Infrastructure'da mı?
- [ ] Linq-over-repo mı kullanılıyor yoksa specific method mu? Specific method tercih.

## Çıktı Formatı

```
🏛️ DDD Review — <component>

✅ Doğru uygulamalar:
  - ...

⚠️ Sorunlu noktalar:
  - [<sev>] <detay> — <öneri>

🚫 Kırmızı çizgi ihlali:
  - <sorun>: <neden>
```

## Kural

- Anemic model + aggregate dışı mutation = otomatik blocker.
- ADR ile karar verilmiş bir pattern'ı ihlal etme — ADR'yi güncelle önce.

## Kaynak

- https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/
- Eric Evans, DDD: Tackling Complexity in the Heart of Software
- Vaughn Vernon, Implementing Domain-Driven Design
