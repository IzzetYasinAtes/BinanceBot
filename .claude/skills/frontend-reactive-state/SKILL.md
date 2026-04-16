---
name: frontend-reactive-state
description: Vue 3 reactive() / ref() / computed() / watch() kullanım rehberi. Page-scoped state (Pinia YOK), component composition, basit pub-sub (reactive global object). frontend-dev agent'ın state yönetirken kullandığı skill.
---

# frontend-reactive-state

Küçük-orta projede Pinia/Vuex overkill. Vue 3 Reactivity API yeterli.

## Temel API

```javascript
import { ref, reactive, computed, watch, onMounted, onUnmounted } from 'vue';

// ref — primitive value wrapper (.value ile erişim)
const count = ref(0);
count.value++;

// reactive — obje/array için proxy (doğrudan field erişimi)
const state = reactive({ klines: [], loading: false, error: null });
state.loading = true;

// computed — türetilmiş değer
const activeKlines = computed(() => state.klines.filter(k => k.isActive));

// watch — side-effect on change
watch(() => state.klines, (newKlines) => {
  console.log('Kline count:', newKlines.length);
}, { deep: true });
```

## Page-Scoped State Pattern

Her sayfa kendi state'ini `setup()`'ta üretir, child component'lara prop olarak geçer. Küresel store YOK.

```javascript
export default {
  name: 'KlinesPage',
  template: `
    <section>
      <kline-toolbar :symbol="state.symbol" @change-symbol="state.symbol = $event" />
      <kline-table :rows="state.klines" :loading="state.loading" />
    </section>
  `,
  setup() {
    const state = reactive({
      symbol: 'BTCUSDT',
      klines: [],
      loading: false,
    });

    async function fetchKlines() {
      state.loading = true;
      const r = await api.get(`/api/klines?symbol=${state.symbol}`);
      if (r.ok) state.klines = r.data;
      state.loading = false;
    }

    watch(() => state.symbol, fetchKlines);
    onMounted(fetchKlines);

    return { state };
  }
};
```

## Basit Cross-Page Pub-Sub (gerekirse)

```javascript
// src/Frontend/js/bus.js
import { reactive } from 'vue';

export const bus = reactive({
  authenticatedUser: null,
  lastNotification: null,
});
```

Her sayfa `import { bus } from '/js/bus.js'` ile erişir. Write ve read reactive. **En fazla 3-5 property** burada; şişerse Pinia düşünülür (ama CDN'de Pinia import etmek mümkün, yine de sakın).

## Kurallar

- **`data() { return {...} }` YASAK** — Options API karıştırma, Composition API disiplinli.
- `ref` vs `reactive`: primitive → `ref`, obje/array → `reactive`. İkisi karıştırılmaz.
- Watch'ta deep option maliyet — gerekmedikçe kullanma.
- Component kendi state'ine yazar; parent'ın state'ini props üstünden mutate etme — `emit` ile event gönder.

## Lifecycle Hooks

- `onMounted(fn)` — DOM mount sonrası ilk çağrı
- `onUnmounted(fn)` — temizlik (timer clear, event listener remove)
- `onUpdated(fn)` — reactive değişim sonrası re-render (nadir kullanılır)

## Kaynak

- https://vuejs.org/guide/scaling-up/state-management.html#simple-state-management-with-reactivity-api
- https://vuejs.org/api/reactivity-core.html
