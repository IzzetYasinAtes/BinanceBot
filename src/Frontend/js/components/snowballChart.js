// SnowballChart — kartopu büyüme mini area chart.
// Prop: equityHistory = [{ ts, equity }], startingBalance, current.
// SVG ile normalize edilmiş area path + başlangıç referans çizgisi.

import { computed } from "vue";
import { fmt } from "../format.js";

const W = 320;
const H = 140;
const PAD_X = 8;
const PAD_Y = 10;

function buildPaths(points, startRef) {
    if (!Array.isArray(points) || points.length < 2) {
        return { line: "", area: "", refY: null };
    }
    const vals = points.map(p => Number(p.equity)).filter(Number.isFinite);
    if (vals.length < 2) return { line: "", area: "", refY: null };

    const min = Math.min(...vals, Number(startRef) || vals[0]);
    const max = Math.max(...vals, Number(startRef) || vals[0]);
    const span = Math.max(1e-9, max - min);

    const usableW = W - PAD_X * 2;
    const usableH = H - PAD_Y * 2;
    const stepX = usableW / (vals.length - 1);

    const xy = vals.map((v, i) => {
        const x = PAD_X + i * stepX;
        const y = PAD_Y + usableH - ((v - min) / span) * usableH;
        return [x, y];
    });

    const line = xy.map(([x, y], i) =>
        (i === 0 ? "M" : "L") + x.toFixed(2) + "," + y.toFixed(2)
    ).join(" ");

    const lastX = xy[xy.length - 1][0];
    const firstX = xy[0][0];
    const area = line + ` L ${lastX.toFixed(2)},${(PAD_Y + usableH).toFixed(2)}` +
                        ` L ${firstX.toFixed(2)},${(PAD_Y + usableH).toFixed(2)} Z`;

    let refY = null;
    const sb = Number(startRef);
    if (Number.isFinite(sb) && sb >= min && sb <= max) {
        refY = PAD_Y + usableH - ((sb - min) / span) * usableH;
    }

    return { line, area, refY };
}

export const SnowballChart = {
    name: "SnowballChart",
    props: {
        equityHistory:   { type: Array,  default: () => [] },
        startingBalance: { type: Number, default: 100 },
        current:         { type: Number, default: null },
    },
    setup(props) {
        const paths = computed(() => buildPaths(props.equityHistory, props.startingBalance));

        const trendClass = computed(() => {
            const c = Number(props.current);
            const s = Number(props.startingBalance);
            if (!Number.isFinite(c) || !Number.isFinite(s) || s === 0) return "neutral";
            if (c > s) return "good";
            if (c < s) return "bad";
            return "neutral";
        });

        const deltaAbs = computed(() => {
            const c = Number(props.current);
            const s = Number(props.startingBalance);
            if (!Number.isFinite(c) || !Number.isFinite(s)) return null;
            return c - s;
        });

        const deltaPct = computed(() => {
            const c = Number(props.current);
            const s = Number(props.startingBalance);
            if (!Number.isFinite(c) || !Number.isFinite(s) || s === 0) return null;
            return (c - s) / s;
        });

        const sampleCount = computed(() => props.equityHistory?.length ?? 0);

        return { paths, trendClass, deltaAbs, deltaPct, sampleCount, fmt, W, H };
    },
    template: `
        <div class="snowball-card card card-static" :class="'snowball-' + trendClass">
            <div class="card-head">
                <h3 class="card-title">Kartopu</h3>
                <span class="badge" :class="trendClass === 'good' ? 'up' : trendClass === 'bad' ? 'down' : ''">
                    {{ deltaPct != null ? fmt.pctFracSigned(deltaPct) : '—' }}
                </span>
            </div>

            <div class="snowball-svg-wrap">
                <svg :viewBox="'0 0 ' + W + ' ' + H" preserveAspectRatio="none" class="snowball-svg">
                    <defs>
                        <linearGradient id="snowGradGood" x1="0" y1="0" x2="0" y2="1">
                            <stop offset="0%"   stop-color="#10b981" stop-opacity="0.45" />
                            <stop offset="100%" stop-color="#10b981" stop-opacity="0" />
                        </linearGradient>
                        <linearGradient id="snowGradBad" x1="0" y1="0" x2="0" y2="1">
                            <stop offset="0%"   stop-color="#ef4444" stop-opacity="0.45" />
                            <stop offset="100%" stop-color="#ef4444" stop-opacity="0" />
                        </linearGradient>
                        <linearGradient id="snowGradNeutral" x1="0" y1="0" x2="0" y2="1">
                            <stop offset="0%"   stop-color="#6366f1" stop-opacity="0.35" />
                            <stop offset="100%" stop-color="#6366f1" stop-opacity="0" />
                        </linearGradient>
                    </defs>

                    <!-- Başlangıç bakiye referans çizgisi -->
                    <line v-if="paths.refY != null"
                          :x1="0" :x2="W"
                          :y1="paths.refY" :y2="paths.refY"
                          stroke="rgba(255,255,255,0.18)"
                          stroke-width="1"
                          stroke-dasharray="3 3" />

                    <template v-if="paths.line">
                        <path class="snowball-area"
                              :d="paths.area"
                              :fill="trendClass === 'good' ? 'url(#snowGradGood)'
                                   : trendClass === 'bad'  ? 'url(#snowGradBad)'
                                                           : 'url(#snowGradNeutral)'" />
                        <path class="snowball-line"
                              :d="paths.line"
                              fill="none"
                              :stroke="trendClass === 'good' ? '#34d399'
                                     : trendClass === 'bad'  ? '#f87171'
                                                             : '#818cf8'"
                              stroke-width="2"
                              stroke-linecap="round"
                              stroke-linejoin="round" />
                    </template>
                </svg>

                <div v-if="!paths.line" class="snowball-empty muted tiny">
                    Veri toplanıyor… ({{ sampleCount }} örnek)
                </div>
            </div>

            <div class="snowball-foot">
                <div class="kv">
                    <div class="k">Başlangıç</div>
                    <div class="v mono">{{ fmt.money(startingBalance) }}</div>
                </div>
                <div class="kv">
                    <div class="k">Şu an</div>
                    <div class="v mono">{{ current != null ? fmt.money(current) : '—' }}</div>
                </div>
                <div class="kv">
                    <div class="k">Fark</div>
                    <div class="v mono" :class="fmt.sign(deltaAbs)">
                        {{ deltaAbs != null ? fmt.moneySigned(deltaAbs) : '—' }}
                    </div>
                </div>
            </div>
        </div>
    `,
};
