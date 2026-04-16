---
name: reviewer-solid-check
description: SOLID prensiplerinin ihlal paternlerini tarar — SRP (God class/method), OCP (switch-type kokusu), LSP (base class contract ihlali), ISP (fat interface), DIP (concrete'e bağımlılık). reviewer agent'ın kapsamlı tasarım denetimi için.
---

# reviewer-solid-check

Sadece "prensibi bil" yetmez — ihlal paternlerini gözden geçir.

## SRP — Single Responsibility

**Kokular:**
- Class adı "Manager", "Processor", "Helper", "Util" → genelde SRP ihlali.
- Metot 100+ satır — iki iş yapıyor büyük ihtimalle.
- Aynı class hem DB yazıyor hem HTTP çağırıyor hem logging yapıyor → ayır.

**Fix:** sorumlulukları ayrı class'lara böl, DI ile birleştir.

## OCP — Open/Closed

**Kokular:**
- `switch (type)` veya `if (x is TypeA) ... else if (x is TypeB)` — her yeni tip için source modify gerektiriyor.
- `enum KlineType { A, B, C }` + bu enum'a göre davranış switch'i — polymorphism'i kaçırmış.

**Fix:** strategy pattern / polymorphism / dictionary<enum, handler>.

## LSP — Liskov Substitution

**Kokular:**
- Derived class parent metodunu `NotSupportedException` ile override ediyor.
- Parent contract "non-null döner" derken child null dönüyor.
- Base'de `virtual void Save()` çağırıldığında derived `Save` pre/post-condition'ı daraltıyor.

**Fix:** is-a ilişkisi gerçekten var mı? Yoksa composition.

## ISP — Interface Segregation

**Kokular:**
- `IRepository<T>` 15 metod içeriyor, concrete'te 3'ü kullanılıyor.
- Mock yazarken 10 metod mock'lamak zorunda kalıyorsun.

**Fix:** küçük interface'ler (`IReader<T>`, `IWriter<T>`, `IQuery<T>`).

## DIP — Dependency Inversion

**Kokular:**
- `new HttpClient()` handler'da — IHttpClientFactory'i kaçırıyor.
- `new DbContext()` — DI'ı bypass.
- Class doğrudan concrete implementation'a referans (`var x = new KlineRepository(...)`) yerine interface'i enjekte etmeli.

**Fix:** ctor injection + interface soyutlama.

## Sık Görülen Kombineler

- **God Handler:** IngestKlineHandler içinde 400 satır → DB + HTTP + Kafka + Cache + Logging. SRP + DIP ihlali. Domain event ile böl.
- **Hard-coded concrete + new:** DIP ihlali. Çözüm: `IWsClient` + DI.

## Çıktı

```
⚙️ SOLID Check: <file>

SRP: <dosya>:<line> — <koku>: <fix önerisi>
OCP: ...
LSP: ...
ISP: ...
DIP: ...
```

## Kural

- İhlal → severity "⚠️ minor" default; ciddi ise "🚫 blocker".
- Önerinin birim kodu göster (snippet).
- Toleranslı ol — over-engineering pahasına SOLID zorlama; iş kuralı basitse basit kalsın.

## Kaynak

- Robert C. Martin, "Design Principles and Design Patterns"
- https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/architectural-principles
