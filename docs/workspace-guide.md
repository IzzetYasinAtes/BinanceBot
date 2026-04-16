# Workspace Kullanım Rehberi

BinanceBot AI workspace'inde **nasıl çalışacağın** — PM'le nasıl konuşursun, agent'ları nasıl çağırırsın, log'u nereden okursun.

## Hızlı Başlangıç

1. **İlk kurulum:**
   ```bash
   dotnet build -c Release tools/mcp-agent-bus/mcp-agent-bus.csproj
   # Node + jq kontrol
   node --version   # v18+
   # (jq opsiyonel — trace.sh node kullanır)
   # Playwright MCP ilk çalıştırmada browser indirir
   npx -y @playwright/mcp@latest --version
   ```
2. **Claude Code'u aç** — bu repo içinde. Otomatik:
   - `CLAUDE.md` yüklenir.
   - `.mcp.json`'daki 3 MCP server başlatılır (agent-bus, playwright, memory).
   - `.claude/settings.json` hook'ları aktifleşir.
3. **Konuş:** PM (Proje Yöneticisi) karşılar. Türkçe yaz.

## Nasıl İş Ver?

PM'e direkt konuş:
> "Binance'ten BTC 1m kline stream'ini DB'ye yaz."

PM şunu yapacak:
1. `pm-decompose` — görevi ≤5 chunk'a böler.
2. Her chunk için `pm-handoff` + Task tool ile uzman çağırır.
3. Her chunk sonunda özet + "devam / `/status` / `/tell-pm <not>` / dur?" sorusu.

## Mid-Task Not Bırakma

Çalışırken fikrin değişirse:
```
/tell-pm ETH'yi şimdilik erteleyelim
```

Bu mesaj MCP agent-bus'ın user-notes kuyruğuna düşer. PM **bir sonraki checkpoint'te** okur ve planı günceller. Mid-task chat native desteklenmediği için **önceki chunk bitene kadar PM görmez**; bu tasarım kararıdır.

## Anlık Durum

```
/status
```

PM `pm-status` skill'i ile MCP'den:
- TodoWrite task listesi
- Son 5 handoff
- Consume edilmemiş user-notes
- Son 3 subagent completion özeti
çeker. Tek ekranlık özet.

## Agent Zinciri (default)

Kullanıcı → **PM** → (kripto varsa önce **binance-expert**) → **architect** (gerekirse) → **backend-dev** / **frontend-dev** → **tester** → **reviewer** → PM özet → Kullanıcı

## Escape / Resume

- Çalışırken durdurmak: **Escape** basılır.
- Session kapanır. State `.ai-trace/` ve MCP `task-state.json`'da.
- Yeniden `claude` aç → PM son task state'i okur ve nerede kaldığını söyler.

## Audit Trail

Her şey `.ai-trace/` altında:
- `decisions.jsonl` — karar logu (commited).
- `handoffs.jsonl` — agent devirleri (commited).
- `user-notes.jsonl` — `/tell-pm` notları (commited).
- `subagent-stops/*.md` — her subagent completion özeti (commited).
- `sessions/`, `tool-calls/` — ham payload (ignored).

Sorgu örnekleri:
```bash
# Son 20 karar
tail -20 .ai-trace/decisions.jsonl | node -e "let l=require('fs').readFileSync(0,'utf8');l.split('\n').filter(Boolean).forEach(j=>console.log(JSON.parse(j)))"

# Hangi agent ne kadar karar verdi
cat .ai-trace/decisions.jsonl | grep -o '"agent":"[^"]*"' | sort | uniq -c

# Tüm ADR'ler
ls docs/adr/
```

## Agent Görev Dağılımı

| Agent | Model | MCP | Yetki |
|---|---|---|---|
| `pm` | opus | agent-bus | Orchestrate + user conversation |
| `architect` | opus | agent-bus, memory | ADR + DDD/Clean/CQRS sınır |
| `backend-dev` | opus | agent-bus | src/** + tools/mcp-agent-bus/** |
| `frontend-dev` | opus | agent-bus | src/Frontend/** |
| `binance-expert` | opus | agent-bus, memory | Kripto domain danışmanı (zorunlu) |
| `reviewer` | opus | agent-bus | Read-only diff review |
| `tester` | sonnet | agent-bus, playwright | Playwright UI + DB + API test |

## Skill Çağırma

- **Otomatik**: skill description agent'ın context'inde; task'a göre Claude çağırır.
- **Manuel (user-invocable)**: `/tell-pm <msg>` global; `/status` PM'de.

Her agent'ın skill'lerine agent'ın frontmatter'ındaki `skills:` listesi bakar.

## Ortak Senaryolar

### Yeni Feature

```
> "X feature'ı yap"
PM: plan çıkarır → architect (gerekirse) → backend-dev + frontend-dev → tester → reviewer → done
```

### Hızlı Soru

```
> "EF migration nasıl eklerim?"
PM: "bu teknik bir soru, backend-dev skill `backend-ef-migration` yönlendirir" + cevap
(ya da PM direkt skill'i kendi context'inde tüketir — kod yazmayacaksa)
```

### Kripto Fikri Değerlendirme

```
> "RSI < 30'da al, > 70'te sat — bu strateji mantıklı mı?"
PM → binance-expert → binance-trading-strategy-review → red flag raporu
```

### Bug Raporu

```
> "Kline ingestion duplicate yazıyor"
PM → tester (tester-db-sanity) → duplicate var mı? → reviewer (reviewer-ws-resiliency) → handler idempotent mi?
  → backend-dev düzeltir → tester tekrar koşar → reviewer onay → done
```

## Ne Yapmamalısın

- PM'i bypass etmek: direkt "architect bana ADR yaz" deme — PM'e söyle, o yönlendirsin.
- Agent Teams (experimental) açmak: workspace kontrol kaybolur.
- `.ai-trace/` içine manuel yazmak: her şey MCP'den geçsin.
- Production Binance'e bağlanmak: testnet/sandbox only.

## Sık Sorulan

**S: `/status` yazınca cevap yok?**
C: MCP agent-bus çalışmıyor olabilir. `dotnet build tools/mcp-agent-bus` sonra Claude restart.

**S: Playwright MCP timeout veriyor.**
C: İlk çalıştırmada Chromium indirilir. `npx -y @playwright/mcp@latest --version` bir kez manuel çalıştır.

**S: Hook'larda `jq` hatası alıyorum.**
C: `trace.sh` node kullanır, jq değil. Node 18+ yüklü mü?

**S: `.ai-trace/decisions.jsonl` git merge conflict verdi.**
C: JSONL append-only — her iki branch'teki satırları koru. İleride günlük-partition'a geçeriz (plan bölüm 13/8).

## Bağlantılar

- Plan: `C:\Users\iyasi\.claude\plans\bir-principal-ai-engineering-glistening-boot.md`
- Kaynaklar: `docs/sources/REFERENCES.md`
- Glossary: `docs/glossary.md`
- Log şeması: `.ai-trace/README.md`
