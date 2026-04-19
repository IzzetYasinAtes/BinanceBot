// Mum Grafikleri — TradingView lightweight-charts candlestick + volume histogram.

import { createApp, ref, computed, watch, onMounted, onBeforeUnmount, nextTick } from "vue";
import { createChart, CrosshairMode } from "lightweight-charts";
import { api } from "../api.js";
import { fmt } from "../format.js";
import { Sidebar, ErrorBanner } from "../ui.js";

const SYMBOLS = ["BTCUSDT", "ETHUSDT", "BNBUSDT", "XRPUSDT"];
const INTERVALS = [
    { id: "1m", label: "1m" },
    { id: "5m", label: "5m" },
    { id: "15m", label: "15m" },
    { id: "1h", label: "1s" },
];

function toSec(v) {
    const t = typeof v === "number" ? v : new Date(v).getTime();
    return Math.floor(t / 1000);
}

function mapCandles(bars) {
    return bars.map(b => ({
        time: toSec(b.openTime ?? b.time ?? b.openedAt),
        open: Number(b.open),
        high: Number(b.high),
        low: Number(b.low),
        close: Number(b.close),
    })).sort((a, b) => a.time - b.time);
}

function mapVolumes(bars) {
    return bars.map(b => ({
        time: toSec(b.openTime ?? b.time ?? b.openedAt),
        value: Number(b.volume ?? b.baseVolume ?? 0),
        color: Number(b.close) >= Number(b.open)
            ? "rgba(16, 185, 129, 0.55)"
            : "rgba(239, 68, 68, 0.55)",
    })).sort((a, b) => a.time - b.time);
}

const App = {
    components: { Sidebar, ErrorBanner },
    template: `
        <div class="app">
            <Sidebar active="klines" />
            <main>
                <div class="page-header">
                    <h1 class="page-title">Mum Grafikleri</h1>
                    <p class="page-sub">Seçilen sembol için 1 dakikalık mumlar ve hacim histogramı, otomatik yenilemeli.</p>
                </div>

                <ErrorBanner :error="errorObj" />

                <section class="block">
                    <div class="row-between">
                        <div class="chip-group">
                            <button v-for="s in symbols" :key="s"
                                    class="chip" :class="{ active: symbol === s }"
                                    @click="symbol = s">
                                {{ fmt.baseAsset(s) }}
                            </button>
                        </div>
                        <div class="chip-group">
                            <button v-for="i in intervals" :key="i.id"
                                    class="chip" :class="{ active: interval === i.id }"
                                    @click="interval = i.id">
                                {{ i.label }}
                            </button>
                        </div>
                    </div>
                </section>

                <div class="chart-wrap fade-in">
                    <div class="ticker-line">
                        <span class="sym">{{ symbol }}</span>
                        <span class="price" :class="tickClass">
                            {{ lastPrice ? '$' + fmt.price(lastPrice) : '—' }}
                        </span>
                        <span class="badge" :class="changeCls" v-if="changePct != null">
                            {{ fmt.pctSigned(changePct) }}
                        </span>
                        <span class="muted small">{{ interval }} · canlı yenileme {{ refreshMs/1000 }}sn</span>
                    </div>
                    <div ref="chartEl" class="chart-canvas"></div>
                </div>
            </main>
        </div>
    `,
    setup() {
        const urlSym = new URL(window.location.href).searchParams.get("symbol");
        const symbol   = ref(SYMBOLS.includes(urlSym) ? urlSym : "BTCUSDT");
        const interval = ref("1m");
        const chartEl  = ref(null);
        const lastPrice = ref(null);
        const prevPrice = ref(null);
        const changePct = ref(null);
        const tickClass = ref("");
        const errorObj = ref(null);
        const refreshMs = 5000;

        let chart = null;
        let candleSeries = null;
        let volumeSeries = null;
        let timer = null;
        let resizeObs = null;

        async function load(initial = false) {
            try {
                const bars = await api.klines(symbol.value, interval.value, 500);
                if (!Array.isArray(bars) || bars.length === 0) return;
                const candles = mapCandles(bars);
                const volumes = mapVolumes(bars);
                candleSeries.setData(candles);
                volumeSeries.setData(volumes);

                const last = candles[candles.length - 1];
                const first = candles[0];
                const prev = lastPrice.value;
                prevPrice.value = prev;
                lastPrice.value = last.close;
                changePct.value = first.close > 0
                    ? ((last.close - first.close) / first.close) * 100
                    : 0;
                if (prev !== null) {
                    if (last.close > prev) { tickClass.value = "metric-good flash-up"; }
                    else if (last.close < prev) { tickClass.value = "metric-bad flash-down"; }
                    setTimeout(() => { tickClass.value = ""; }, 700);
                }
                errorObj.value = null;
                if (initial) chart.timeScale().fitContent();
            } catch (e) {
                errorObj.value = e;
            }
        }

        function initChart() {
            const el = chartEl.value;
            chart = createChart(el, {
                width: el.clientWidth,
                height: el.clientHeight,
                layout: {
                    background: { type: "solid", color: "transparent" },
                    textColor: "#b5bdd1",
                    fontFamily: "ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto",
                },
                grid: {
                    vertLines: { color: "rgba(255,255,255,0.04)" },
                    horzLines: { color: "rgba(255,255,255,0.05)" },
                },
                rightPriceScale: { borderColor: "rgba(255,255,255,0.1)" },
                timeScale:       { borderColor: "rgba(255,255,255,0.1)", timeVisible: true, secondsVisible: false },
                crosshair:       { mode: CrosshairMode.Normal },
            });

            candleSeries = chart.addCandlestickSeries({
                upColor: "#10b981",
                downColor: "#ef4444",
                borderUpColor: "#10b981",
                borderDownColor: "#ef4444",
                wickUpColor: "#34d399",
                wickDownColor: "#f87171",
            });

            volumeSeries = chart.addHistogramSeries({
                color: "rgba(99,102,241,0.4)",
                priceFormat: { type: "volume" },
                priceScaleId: "vol",
            });
            chart.priceScale("vol").applyOptions({
                scaleMargins: { top: 0.82, bottom: 0 },
            });

            resizeObs = new ResizeObserver(() => {
                if (!chart || !el) return;
                chart.applyOptions({ width: el.clientWidth, height: el.clientHeight });
            });
            resizeObs.observe(el);
        }

        onMounted(async () => {
            await nextTick();
            initChart();
            await load(true);
            timer = setInterval(load, refreshMs);
        });

        onBeforeUnmount(() => {
            if (timer) clearInterval(timer);
            if (resizeObs) resizeObs.disconnect();
            if (chart) { chart.remove(); chart = null; }
        });

        watch([symbol, interval], () => load(true));

        const changeCls = computed(() => {
            const c = changePct.value;
            if (c == null) return "";
            return c >= 0 ? "up" : "down";
        });

        return {
            symbol, interval, chartEl, lastPrice, changePct, tickClass, errorObj,
            symbols: SYMBOLS, intervals: INTERVALS, refreshMs, changeCls, fmt,
        };
    },
};

createApp(App).mount("#app");
