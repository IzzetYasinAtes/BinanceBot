---
name: reviewer-diff-review
description: git diff üzerinden dosya dosya kod inceleme. CLAUDE.md kurallarını, projenin pattern'lerini (Result<T>, CQRS, Vue CDN) ve genel kod kalitesini (naming, duplication, comments) denetler. reviewer agent'ının ana inceleme skill'i.
---

# reviewer-diff-review

Her değişen dosyayı aç, satır satır oku, yorum yaz.

## Sıra

1. `git diff main --stat` — değişen dosya listesi + line sayıları.
2. `git diff main -- <file>` — dosya bazlı diff.
3. Her dosya için:
   - Yeni mi / değiştirilmiş mi?
   - Path doğru yerde mi? (backend → `src`, frontend → `src/Frontend`)
   - CLAUDE.md kurallarını ihlal ediyor mu? (npm, lazy loading, throw-for-flow, vb.)
   - Pattern uyumu? (Result<T>, CQRS, Polly, reactive())
4. Satır yorumu:
   - Format: `<file>:<line> — <yorum>`
   - Severity: 🚫 blocker / ⚠️ minor / 💡 suggestion / ✅ praise

## Tarama Konuları

### Backend
- [ ] Command/Query ayrımı var mı? (bkz. `architect-cqrs-design`)
- [ ] Handler'da iş kuralı yerine domain aggregate'e delege mi?
- [ ] `Result<T>` dönüyor mu, yoksa exception for flow mu?
- [ ] `AsNoTracking()` read path'te var mı?
- [ ] `IHttpClientFactory` kullanılmış mı, yoksa `new HttpClient()` mı? (YASAK)
- [ ] `async` metodlarda `CancellationToken` parametre + zincirleme?
- [ ] Magic string/number var mı? (const veya config'e al)
- [ ] Naming convention (PascalCase class, camelCase local, _prefix private field)?

### Frontend
- [ ] npm/package.json eklendi mi? (YASAK — production için)
- [ ] SFC (`<script setup>`, `.vue` dosya) var mı? (YASAK)
- [ ] `fetch()` direkt mi, yoksa `api.*` wrapper mı?
- [ ] Pinia/Vuex import mı? (YASAK)
- [ ] Importmap'te pin'siz sürüm var mı? (`@latest` YASAK)

### Genel
- [ ] Commented-out kod bırakılmış mı? (sil)
- [ ] TODO/FIXME atılmış mı? (task id'siyle açıkla veya çıkar)
- [ ] Test var mı? Yeni handler + sıfır test → minor warn.
- [ ] Secret/key commit edilmiş mi? 🚫 blocker.

## Çıktı Örneği

```
📋 Review: feat/ingest-klines branch'i

Kural İhlalleri:
  🚫 [blocker] src/Application/Klines/IngestKlineCommandHandler.cs:47
     — Exception-for-flow yasak. `throw new InvalidOperationException` yerine `Result.Invalid(...)` kullan.
  ⚠️ [minor] src/Frontend/js/pages/klines.js:23
     — `fetch('/api/...')` direkt çağrı. `api.get(...)` wrapper'ından geçir.
  💡 [sug] src/Domain/Klines/Kline.cs:12
     — Constructor'da validation var ama Result<T> dönmüyor; static factory (`Kline.Create(...)`) Result<Kline> dönebilir.

İyi Yanlar:
  ✅ src/Infrastructure/MarketData/BinanceWsSupervisor.cs — reconnect+backoff+jitter doğru uygulanmış.

WS Resiliency:
  ✓ Exponential backoff
  ✓ Subscribe replay
  ✓ Handler idempotent (unique index Symbol+OpenTime)
  □ Pong timeout explicit check — var olsa iyi olur.

Verdict: 🚫 blocker — IngestKlineCommandHandler exception düzeltilmeli.
```

## Kural

- Blocker varsa verdict "🚫 blocker". Yoksa minor/sug sayısına göre "⚠️ minor" / "✅ onay".
- Praise (`✅`) de yaz — sadece eleştiri reviewer'ı güvensiz gösterir.
- Yorumu agent'a değil koda yap ("IngestKlineCommandHandler exception" değil, "47. satır").
