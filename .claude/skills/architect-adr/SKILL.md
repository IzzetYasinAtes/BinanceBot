---
name: architect-adr
description: Mimari Karar Kaydı (ADR) üretir. docs/adr/NNNN-<slug>.md dosyası MADR formatında — Context/Decision/Consequences/Alternatifler/Kaynak. Her major mimari karar ADR olarak kaydedilir ki gelecekte "niye böyle yapılmış" sorusu cevaplanabilsin.
---

# architect-adr

Mimari kararı kalıcı hale getirir. Git history tek başına "neden" sorusunu cevaplamaz — ADR bu boşluğu doldurur.

## Ne zaman ADR yaz?

- Yeni aggregate / bounded context eklenince
- Teknoloji seçimi (Polly vs custom, MediatR vs direct handler)
- Pattern değişimi (Result<T> yerine exception-based'e dönülecekse — yapma, ama ADR konusu)
- Dependency yönünü değiştiren refactor
- Cross-cutting endişe eklenmesi (Auth, Caching, Logging stratejisi)

ADR yazma: tek dosya refactor, küçük bug fix, kozmetik.

## Sıra

1. `docs/adr/` içindeki son dosyanın numarasını oku (Glob).
2. Yeni numara = son + 1 (4 digit zero-pad: 0001, 0002, ...).
3. Slug: başlıktan lowercase-kebab-case (`add-kline-aggregate`).
4. Dosya: `docs/adr/<NNNN>-<slug>.md`.
5. MADR template'ini doldur:

```markdown
# <NNNN>. <Başlık>

Date: <YYYY-MM-DD>
Status: Proposed

## Context
<Problemi, kısıtı, hangi senaryonun bu kararı gerektirdiğini 3-5 cümlede anlat.>

## Decision
<Ne karar verildi — tek cümle + 1 paragraf gerekçe.>

## Consequences

### Pozitif
- <etki 1>
- <etki 2>

### Negatif / Tradeoff
- <neyi feda ettik>

### Nötr
- <diğer etki>

## Alternatifler
1. **<Alternatif A>** — Reddedildi: <neden>
2. **<Alternatif B>** — Reddedildi: <neden>

## Kaynak
- <link 1>
- Önceki ADR: [NNNN-xxx](./NNNN-xxx.md) (ilgiliyse)
```

6. Status'u "Proposed" olarak yaz; PM kullanıcıdan onay alınca "Accepted"a güncellenir (yeni commit ile).
7. MCP `agent-bus.append_decision` — "ADR <NNNN>: <başlık> — Proposed".

## Kural

- Numara monoton. Silme yasak — değişirse `Superseded by NNNN` ve yeni ADR yaz.
- Türkçe yazılır; kod/identifier/teknoloji ismi İngilizce.
- Alternatifler en az 2 tane olmalı — sadece tek çözüm sunmak şüpheli.
- Consequences'ta "pozitif" kadar "negatif" de olmalı — her kararın fiyatı var.

## Kaynak

- https://adr.github.io/madr/ — MADR resmi
- https://github.com/joelparkerhenderson/architecture-decision-record
