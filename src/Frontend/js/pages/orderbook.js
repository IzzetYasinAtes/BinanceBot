// Emir Defteri — bid/ask ladder, spread vurgusu.

import { createApp, ref, computed, watch } from "vue";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner, usePolling } from "../ui.js";
import { SymbolLogo } from "../components/symbolLogo.js";

const SYMBOLS = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "XRPUSDT"];

const App = {
    components: { Sidebar, ErrorBanner, SymbolLogo },
    template: `
        <div class="app">
            <Sidebar active="orderbook" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Emir Defteri</h1>
                    <p class="page-sub">Binance piyasa derinliği — alış (bid) ve satış (ask) seviyeleri, seviye başına boyut vurgulu.</p>
                </div>

                <ErrorBanner :error="poll.error.value" />

                <section class="block">
                    <div class="chip-group">
                        <button v-for="s in symbols" :key="s"
                                class="chip" :class="{ active: symbol === s }"
                                @click="symbol = s">
                            <SymbolLogo :symbol="s" :size="18" />
                            <span>{{ fmt.baseAsset(s) }}</span>
                        </button>
                    </div>
                </section>

                <!-- Spread -->
                <div class="ob-spread" v-if="book">
                    <div class="lbl">Alış—Satış Farkı (Spread)</div>
                    <div class="val">
                        {{ spreadAbs ? '$' + fmt.price(spreadAbs) : '—' }}
                        <span class="muted small">({{ spreadBps != null ? fmt.num2(spreadBps) + ' bps' : '—' }})</span>
                    </div>
                    <div class="muted tiny mt-2">
                        Orta fiyat {{ midPrice ? '$' + fmt.price(midPrice) : '—' }} · {{ fmt.timeHms(book.capturedAt) }}
                    </div>
                </div>

                <div class="ob-grid">
                    <!-- BIDS -->
                    <div class="ob-side bid">
                        <h3>ALIŞ (Bid) · alan talepleri</h3>
                        <div v-if="!bids" class="skeleton" style="height:400px"></div>
                        <div v-else-if="bids.length === 0" class="empty-state">Veri yok</div>
                        <div v-else>
                            <div class="ob-row" v-for="(r, i) in bids" :key="'b'+i">
                                <span class="bar" :style="{ width: widthOf(r, maxBid) + '%' }"></span>
                                <span class="price good">{{ fmt.price(r.price) }}</span>
                                <span class="qty">{{ fmt.num4(r.quantity) }}</span>
                            </div>
                        </div>
                    </div>

                    <!-- ASKS -->
                    <div class="ob-side ask">
                        <h3>SATIŞ (Ask) · satan teklifleri</h3>
                        <div v-if="!asks" class="skeleton" style="height:400px"></div>
                        <div v-else-if="asks.length === 0" class="empty-state">Veri yok</div>
                        <div v-else>
                            <div class="ob-row" v-for="(r, i) in asks" :key="'a'+i">
                                <span class="bar" :style="{ width: widthOf(r, maxAsk) + '%' }"></span>
                                <span class="price bad">{{ fmt.price(r.price) }}</span>
                                <span class="qty">{{ fmt.num4(r.quantity) }}</span>
                            </div>
                        </div>
                    </div>
                </div>
            </main>
        </div>
    `,
    setup() {
        const symbol = ref("BTCUSDT");
        const poll = usePolling(() => api.orderbook.snapshot(symbol.value, 20), 2500);
        watch(symbol, () => poll.refresh());

        const book = computed(() => poll.data.value || null);

        const bids = computed(() => {
            const b = book.value?.bids;
            return Array.isArray(b) ? b.slice(0, 20).map(x => ({
                price: Number(x.price),
                quantity: Number(x.quantity),
            })) : null;
        });
        const asks = computed(() => {
            const a = book.value?.asks;
            return Array.isArray(a) ? a.slice(0, 20).map(x => ({
                price: Number(x.price),
                quantity: Number(x.quantity),
            })) : null;
        });

        const maxBid = computed(() => {
            if (!bids.value) return 0;
            return bids.value.reduce((m, r) => Math.max(m, r.quantity), 0);
        });
        const maxAsk = computed(() => {
            if (!asks.value) return 0;
            return asks.value.reduce((m, r) => Math.max(m, r.quantity), 0);
        });

        function widthOf(r, max) {
            if (!max || !r?.quantity) return 0;
            return Math.min(100, (r.quantity / max) * 100);
        }

        const bestBid = computed(() => bids.value?.[0]?.price || null);
        const bestAsk = computed(() => asks.value?.[0]?.price || null);
        const spreadAbs = computed(() => {
            if (bestBid.value == null || bestAsk.value == null) return null;
            return bestAsk.value - bestBid.value;
        });
        const midPrice = computed(() => {
            if (bestBid.value == null || bestAsk.value == null) return null;
            return (bestAsk.value + bestBid.value) / 2;
        });
        const spreadBps = computed(() => {
            if (spreadAbs.value == null || midPrice.value == null || midPrice.value <= 0) return null;
            return (spreadAbs.value / midPrice.value) * 10_000;
        });

        return {
            symbol, symbols: SYMBOLS, poll, book,
            bids, asks, maxBid, maxAsk, widthOf,
            spreadAbs, spreadBps, midPrice, fmt,
        };
    },
};

createApp(App).mount("#app");
