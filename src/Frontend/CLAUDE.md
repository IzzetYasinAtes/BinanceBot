# src/Frontend/ — CLAUDE.md

Bu dosya `src/Frontend/**` altındaki herhangi bir dosya okunduğunda yüklenir. Frontend-dev agent'ı buraya tabidir.

## Temel Kısıtlar

- **Vue 3 CDN** — `importmap` ile modül çözümü.
- **NPM YOK** — `package.json`, `node_modules/`, `package-lock.json` frontend için yasak.
- **Bundler YOK** — Vite/Webpack/Rollup yok.
- **SFC YOK** — `.vue` dosya yasak. Vanilla JS modül + template string.
- **TypeScript compile YOK** — JSDoc ile type hinting olabilir.

## Layout

```
src/Frontend/
├── index.html              # shell + importmap + root app
├── js/
│   ├── app.js              # createApp + route dispatching
│   ├── api.js              # fetch wrapper (Result<T> sözleşmesi)
│   ├── bus.js              # opsiyonel basit pub-sub (reactive global)
│   ├── pages/
│   │   └── <pageName>.js   # default export: component object
│   └── components/
│       └── <compName>.js   # default export: component object
├── css/
│   └── style.css           # global styles
└── assets/                 # static (resim, font)
```

## Kurallar

1. **Vue 3 Composition API** — `setup()` zorunlu; `data()/methods/computed/watch` options YASAK.
2. **reactive() / ref()** — page-scoped state. Pinia/Vuex import YASAK.
3. **Template string** — `template: \`...\``. SFC `<template>` YASAK.
4. **fetch wrapper** — `api.get/post/put/delete` kullan; raw `fetch()` YASAK.
5. **Import map versiyon pin** — `@latest` YASAK, tam sürüm.
6. **`v-html` sadece güvenilir kaynak** — XSS riski.
7. **CORS** — backend `/api/*`'a pointing; relative URL (`/api/...`) kullan.
8. **Static server ile serve** — dev için `python -m http.server` veya `dotnet run` içinde `app.UseStaticFiles()`.

## Component Adlandırma

- Dosya: kebab-case (`kline-table.js`).
- Component export: PascalCase (`KlineTable`).
- Template'te: `<kline-table>` (Vue otomatik kebab-case çözümü).

## Event Binding

- `@click`, `@input`, `@submit.prevent` — Vue syntax.
- Emit ile parent'a bildir: `emit('change-symbol', newValue)`.

## Lifecycle

- `onMounted(fn)` — initial fetch.
- `onUnmounted(fn)` — timer clear, websocket close.
- `watch(() => state.field, fn)` — reactive değişim tepkisi.

## Hata Durumu UI

Her sayfada üç state:
- **loading** — `v-if="state.loading"` → spinner.
- **error** — `v-else-if="state.error"` → mesaj + retry buton.
- **empty** — `v-else-if="state.items.length === 0"` → boş state mesajı.
- **content** — `v-else` → data.

## Yasaklar

- `npm install`, `package.json`
- `<script setup>`, `.vue` dosya
- Vite / Webpack / Rollup config
- TypeScript compile (`.ts` dosya)
- `document.getElementById` (direkt DOM manipulation) — Vue reactive kullan
- `fetch()` direkt (wrapper'dan geçir)
- Global state (Pinia/Vuex) — `bus.js` çok küçük pub-sub yeterli
