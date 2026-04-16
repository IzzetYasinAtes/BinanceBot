---
name: tester-error-scan
description: dotnet build/test çıktısını ve .ai-trace/ runtime log'larını tarar — warning/error/exception/failed test sayısı, önemli olanları listeler. tester agent feature "done" öncesi hata kalmış mı kontrol eder.
---

# tester-error-scan

Build yeşil demek kod çalışıyor demek değil — warning, deprecated usage, runtime exception'lar kaçar.

## Sıra

### 1. Build Check

```bash
dotnet build /d/repos/BinanceBot 2>&1 | tee /tmp/build.log
```

Tarama:
- [ ] 0 error mü?
- [ ] 0 warning mü? Yoksa hangileri (CS*, NU*, NETSDK*)?
- [ ] Obsolete API kullanımı var mı? (CS0612, CS0618)
- [ ] Nullable reference type warning (CS8600, CS8604)? — bilinçli mi, yoksa leak mi?

### 2. Test Run

```bash
dotnet test /d/repos/BinanceBot 2>&1 | tee /tmp/test.log
```

Tarama:
- [ ] Total / Passed / Failed / Skipped oranı
- [ ] Failed testlerin listesi
- [ ] Skipped sebebi yazılmış mı?
- [ ] Coverage raporu (coverlet varsa) — kritik proje için %70+ mi?

### 3. Runtime Trace

```bash
# Son 2 saatte unhandled exception var mı?
grep -i "unhandled\|exception\|stack trace" .ai-trace/tool-calls/*.jsonl | head -20

# Decision log'da "failed" geçiyor mu?
grep -i '"decision":"[^"]*failed' .ai-trace/decisions.jsonl | tail -10
```

### 4. Agent-Bus Health

```bash
# MCP agent-bus ping
# (Claude session içinde: mcp__agent-bus__ping — ok dönmeli)
# Veya .ai-trace/bus-health.jsonl varsa son entry'lere bak
tail -20 .ai-trace/bus-health.jsonl 2>/dev/null || echo "health log yok (normal)"
```

## Rapor Formatı

```
🔎 Error Scan — <YYYY-MM-DD HH:MM>

Build:       ✅ 0 error, 0 warning
             (veya: ❌ 3 error — <özet>)
Test:        ✅ 47/47 pass (coverage 78%)
             (veya: ❌ 42/45 pass — 3 failed: Test_X, Test_Y, Test_Z)
Runtime:     ✅ son 2h unhandled exception yok
             (veya: ⚠️ 2 exception: NullReferenceException src/...)
Agent-bus:   ✅ sağlıklı
             (veya: ⚠️ 3 uyarı son 24h)

Verdict: ✅ pass / ⚠️ partial / 🚫 fail
```

## Kural

- 🚫 fail: error > 0 VEYA failed test > 0 VEYA unhandled exception > 0.
- ⚠️ partial: warning var ama error yok; skipped test sebebi belirsiz.
- Rapor Türkçe, özet kısa, log dosyalarına atıf ver.

## Kaynak

- https://learn.microsoft.com/en-us/dotnet/core/testing/
- https://github.com/coverlet-coverage/coverlet
