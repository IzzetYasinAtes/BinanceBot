// Symbol Carousel — BTC/ETH/BNB/XRP kartları yan yana kayan grid.
// Her kart: sembol adı, son fiyat, 24h %, mini sparkline (son 60 bar).
// Prop: symbols (string[])

import { ref, computed, onMounted, onBeforeUnmount, watch } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sparkline } from "./sparkline.js";

export const SymbolCarousel = {
    name: "SymbolCarousel",
    components: { Sparkline },
    props: {
        symbols: { type: Array, required: true },
        intervalMs: { type: Number, default: 10_000 },
        onSelect: { type: Function, default: null },
    },
    setup(props) {
        const trackRef = ref(null);
        const cards = ref(props.symbols.map(sym => ({
            symbol: sym,
            base: fmt.baseAsset(sym),
            price: null,
            prevPrice: null,
            changePct: null,
            points: [],
            flash: "",
        })));

        let timer = null;

        async function fetchOne(card) {
            try {
                const bars = await api.klines(card.symbol, "1m", 60);
                if (!Array.isArray(bars) || bars.length < 2) return;
                const last = Number(bars[bars.length - 1].close);
                const first = Number(bars[0].close);
                const change = first > 0 ? ((last - first) / first) * 100 : 0;
                const closes = bars.map(b => Number(b.close));

                const prev = card.price;
                card.prevPrice = prev;
                card.price = last;
                card.changePct = change;
                card.points = closes;
                if (prev !== null && Number.isFinite(prev)) {
                    if (last > prev) { card.flash = "flash-up"; }
                    else if (last < prev) { card.flash = "flash-down"; }
                    setTimeout(() => { card.flash = ""; }, 700);
                }
            } catch { /* swallow — transient */ }
        }

        async function tick() {
            await Promise.all(cards.value.map(fetchOne));
        }

        function scrollBy(delta) {
            if (!trackRef.value) return;
            trackRef.value.scrollBy({ left: delta, behavior: "smooth" });
        }

        onMounted(() => {
            tick();
            timer = setInterval(tick, props.intervalMs);
        });
        onBeforeUnmount(() => { if (timer) clearInterval(timer); });

        watch(() => props.symbols, (next) => {
            cards.value = next.map(sym => ({
                symbol: sym,
                base: fmt.baseAsset(sym),
                price: null, prevPrice: null, changePct: null, points: [], flash: "",
            }));
            tick();
        });

        function trendOf(c) {
            if (c.changePct == null) return "neutral";
            return c.changePct >= 0 ? "good" : "bad";
        }

        function pnlClass(c) {
            if (c.changePct == null) return "metric-neutral";
            return c.changePct >= 0 ? "metric-good" : "metric-bad";
        }

        function handleClick(c) {
            if (typeof props.onSelect === "function") props.onSelect(c.symbol);
        }

        return { cards, trackRef, scrollBy, trendOf, pnlClass, handleClick, fmt };
    },
    template: `
        <div class="carousel">
            <div class="row-between mb-3">
                <div class="muted small">Sürükle / okları kullan</div>
                <div class="carousel-nav">
                    <button class="carousel-btn" type="button" @click="scrollBy(-260)" aria-label="Önceki">&#8249;</button>
                    <button class="carousel-btn" type="button" @click="scrollBy(260)"  aria-label="Sonraki">&#8250;</button>
                </div>
            </div>
            <div class="carousel-track" ref="trackRef">
                <div v-for="c in cards" :key="c.symbol" class="sym-card fade-in" @click="handleClick(c)">
                    <div class="top">
                        <div class="trade-sym">
                            <span class="sym-dot">{{ c.base.slice(0, 3) }}</span>
                            <span>{{ c.base }}/USDT</span>
                        </div>
                        <span class="badge" :class="trendOf(c)">
                            {{ c.changePct == null ? '—' : fmt.pctSigned(c.changePct) }}
                        </span>
                    </div>
                    <div class="price" :class="[pnlClass(c), c.flash]">
                        {{ c.price == null ? '—' : '$' + fmt.price(c.price) }}
                    </div>
                    <div class="spark-box">
                        <Sparkline :points="c.points" :trend="trendOf(c)" v-if="c.points.length" />
                        <div v-else class="skeleton" style="height:100%"></div>
                    </div>
                    <div class="muted tiny">Son 60 dk · 1m</div>
                </div>
            </div>
        </div>
    `,
};
