// Fiyat ticker marquee — üst bar, borsa stili kayan bant.
// CSS @keyframes marquee ile linear loop; fiyatlar 10 sn'de bir refresh.

import { ref, onMounted, onBeforeUnmount } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { SymbolLogo } from "./symbolLogo.js";

const DEFAULT_SYMBOLS = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "XRPUSDT", "SOLUSDT", "DOGEUSDT"];

export const PriceTicker = {
    components: { SymbolLogo },
    template: `
        <div class="price-ticker" v-if="items.length > 0">
            <div class="price-ticker-track">
                <div class="price-ticker-group" v-for="g in 2" :key="g">
                    <div v-for="(t, i) in items" :key="g + '-' + i" class="price-ticker-item">
                        <SymbolLogo :symbol="t.rawSymbol" :size="18" />
                        <span class="pt-sym">{{ t.sym }}</span>
                        <span class="pt-price tabular-nums">{{ fmt.price(t.price) }}</span>
                        <span class="pt-pct tabular-nums" :class="t.pct >= 0 ? 'up' : 'down'">
                            {{ t.pct >= 0 ? '+' : '' }}{{ (t.pct).toFixed(2) }}%
                        </span>
                    </div>
                </div>
            </div>
        </div>
        <div v-else class="price-ticker price-ticker-skeleton">
            <div class="skeleton" style="height:20px; width:100%"></div>
        </div>
    `,
    props: {
        symbols: { type: Array, default: () => DEFAULT_SYMBOLS },
        intervalMs: { type: Number, default: 10000 },
    },
    setup(props) {
        const items = ref([]);
        let timer = null;
        let stopped = false;

        async function tick() {
            if (stopped) return;
            try {
                const data = await api.marketSummary(props.symbols);
                if (stopped) return;
                const arr = Array.isArray(data?.items) ? data.items
                    : Array.isArray(data) ? data : [];
                const mapped = arr.map(normalize).filter(Boolean);
                if (mapped.length > 0) items.value = mapped;
            } catch {
                // sessiz — önceki değerler kalsın.
            }
        }

        function normalize(r) {
            if (!r) return null;
            const sym = r.symbol || r.Symbol;
            const price = Number(r.lastPrice ?? r.markPrice ?? r.price ?? r.close ?? 0);
            const pct = Number(
                r.change24hPct ?? r.changePct24h ?? r.priceChangePercent ?? r.changePct ?? 0
            );
            if (!sym || !isFinite(price)) return null;
            return {
                rawSymbol: sym,
                sym: prettySymbol(sym),
                price,
                pct: isFinite(pct) ? pct : 0,
            };
        }

        function prettySymbol(s) {
            if (s.endsWith("USDT")) return `${s.slice(0, -4)}/USDT`;
            if (s.endsWith("BUSD")) return `${s.slice(0, -4)}/BUSD`;
            return s;
        }

        onMounted(() => {
            tick();
            timer = setInterval(tick, props.intervalMs);
        });

        onBeforeUnmount(() => {
            stopped = true;
            if (timer) clearInterval(timer);
        });

        return { items, fmt };
    },
};
