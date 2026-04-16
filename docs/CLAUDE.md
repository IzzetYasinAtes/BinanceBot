# docs/ — CLAUDE.md

BinanceBot dokümantasyonu. Bu dosya `docs/**` dokunulduğunda yüklenir.

## Dizin

```
docs/
├── CLAUDE.md                  # bu dosya
├── workspace-guide.md         # AI workspace kullanım rehberi (kullanıcı için)
├── adr/
│   └── NNNN-<slug>.md         # Architecture Decision Records (MADR format)
├── features/
│   └── <feature>.md           # feature spec'leri (UL + acceptance + slice listesi)
├── glossary.md                # Ubiquitous Language (TR+EN)
└── sources/
    └── REFERENCES.md          # skill/kural kaynak URL listesi
```

## ADR Formatı (MADR)

Her ADR: `docs/adr/NNNN-<kebab-slug>.md`. `architect-adr` skill'i üretir.

Template: bkz. `.claude/skills/architect-adr/SKILL.md`. Zorunlu alanlar: Context / Decision / Consequences / Alternatifler / Kaynak.

Numaralar **monoton**, silme yasak. Pattern değişirse `Superseded by NNNN` yaz ve yeni ADR.

## Feature Spec Formatı

`docs/features/<feature-slug>.md`:

```markdown
# <Feature Adı>

## Context
<neden bu feature, iş değeri>

## Ubiquitous Language
- **<Term TR>** (<Term EN>): <tanım>

## Acceptance Criteria
- [ ] Kabul 1
- [ ] Kabul 2

## CQRS Slices
- Command: `<Action>Command` → Handler / Validator
- Query: `Get<X>Query` → Handler / Dto

## Dependencies
- architect ADR #NNNN
- binance-expert research: <konu>
- backend-dev skills: <list>
- frontend-dev skills: <list>

## Test Plan
- Playwright senaryosu: <dosya>
- DB sanity: <tablo>
- API contract: <endpoint'ler>
```

## Glossary

`docs/glossary.md` — BinanceBot'un ubiquitous language'ı. Her yeni term buraya eklenir. İki dilli (TR+EN).

## Kurallar

- Tüm doküman Türkçe yazılır; teknik terim + kod orijinal İngilizce.
- Dosya ismi kebab-case (`add-kline-aggregate.md`).
- Internal link: `[NNNN-xxx](./NNNN-xxx.md)`.
- External link: `[<açıklama>](https://...)`.
- Markdown strict — emoji nokta/virgülle birleşmesin.

## Yasaklar

- Doküman içinde secret/key.
- Muğlak "sanırım", "belki" ifadeleri — net ifade.
- Copy-paste kod blokları — mümkünse implementasyona link ver.
