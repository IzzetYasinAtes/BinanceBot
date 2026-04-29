# binance-expert

- ts: 2026-04-29T04:53:34Z
- agent: binance-expert
- session: cc4797da

## Payload

```json
{"session_id":"cc4797da-fa4c-49d6-b19c-a832635e1fb2","transcript_path":"C:\\Users\\iyasi\\.claude\\projects\\D--repos-BinanceBot\\cc4797da-fa4c-49d6-b19c-a832635e1fb2.jsonl","cwd":"D:\\repos\\BinanceBot","permission_mode":"auto","agent_id":"aa6709fd5a3b5b757","agent_type":"binance-expert","hook_event_name":"SubagentStop","stop_hook_active":false,"agent_transcript_path":"C:\\Users\\iyasi\\.claude\\projects\\D--repos-BinanceBot\\cc4797da-fa4c-49d6-b19c-a832635e1fb2\\subagents\\agent-aa6709fd5a3b5b757.jsonl","last_assistant_message":"---\n\n## Özet\n\n**1. Karar:** BB MeanRev 15m mimari sınıra dayandı. `VolumeZScoreThreshold: 0.0` parametresi gerçekte volume filtresini kapatmıyor — kod seviyesinde `VolumeStd20 > 0m` guard hala aktif ve testnet düz hacim rejiminde kronik bloke ediyor. Diğer seçenekler (BTC odak, Donchian 5m, BookTicker) ya kod değişikliği ya da negatif beklenti taşıyor. **Loop 56: EmaScalper1m 5 coin (BTC/ETH/XRP/SOL/ADA), tüm BB MeanRev devre dışı, config-only pivot.**\n\n**2. Parametre seti:** Yukarıda detaylandırıldı. Her coin için `RsiLowerBand: 35`, `RsiUpperBand: 70`, `VolumeMultiplier: 0.8`, `MinAtrPct: 0.0003`, `TpAtrMultiplier: 1.3`, `SlAtrMultiplier: 0.8`, `MaxHoldMinutes: 10`, `CooldownBarsAfterSignal: 2`. BTC `MaxTpPct: 0.006`, diğerleri `0.007-0.008`.\n\n**3. Backend-dev:** Gerekmiyor. EmaScalper1m evaluator mevcut ve kayıtlı. Seed DB'ye upsert yapıyorsa restart yeterli — ama seed davranışını (insert-only vs upsert) backend-dev'e doğrulatın.\n\n**4. DB reset:** HAYIR. +$0.355 realized ve $500.36 equity korunuyor.\n\n**5. Halt eşikleri:** Realized < -$1.00 → halt | 3+ ardışık SL → halt | t30 = 0 sinyal → log incele (EmaScalper warmup 21 bar × 1m = 21dk, backfill ile anında) | t60 = minimum 2 sinyal | t120 = 5+ toplam emit.\n\n**Bonus — BB MeanRev gelecek fix:** `volumeOk` satırı `p.VolumeZScoreThreshold <= 0m || (VolumeStd20 > 0m && volumeZScore > threshold)` olarak düzeltilmeden config ile \"volume filtresi kapat\" mümkün değil."}
```
