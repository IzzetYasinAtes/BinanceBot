// LiveTradeFeed — son kapalı işlemler, yeni trade geldiğinde üstten kayarak giriş.
// Prop: trades (array, en yeni önce)
// Key: pozisyon id — Vue transition-group yeni eklenenleri animate eder.
//
// Her satır: sembol logosu + Long/Short rozet + giriş→çıkış + net PnL + süre + zaman.

import { ref, watch } from "vue";
import { fmt } from "../format.js";
import { SymbolLogo } from "./symbolLogo.js";

export const LiveTradeFeed = {
    name: "LiveTradeFeed",
    components: { SymbolLogo },
    props: {
        trades: { type: Array, default: () => [] },
        maxItems: { type: Number, default: 12 },
    },
    setup(props) {
        // Son görülen id set'i — yeni gelenleri fark et (animasyon kontrolü için).
        const seenIds = ref(new Set());

        watch(
            () => props.trades,
            (arr) => {
                if (!Array.isArray(arr)) return;
                const next = new Set();
                for (const t of arr) {
                    if (t?.id != null) next.add(t.id);
                }
                seenIds.value = next;
            },
            { immediate: true, deep: false },
        );

        function pnlClass(v) {
            return fmt.sign(v);
        }

        function durationLabel(p) {
            return fmt.duration(p.openedAt, p.closedAt);
        }

        function exitLabel(p) {
            return p.exitPrice != null ? fmt.price(p.exitPrice) : "—";
        }

        return { fmt, pnlClass, durationLabel, exitLabel };
    },
    template: `
        <div class="live-trade-feed">
            <transition-group name="trade-slide" tag="div" class="live-trade-list">
                <div v-for="p in trades.slice(0, maxItems)"
                     :key="p.id"
                     class="live-trade-row">
                    <div class="ltr-left">
                        <SymbolLogo :symbol="p.symbol" :size="26" />
                        <div class="ltr-sym">
                            <div class="ltr-sym-name">{{ p.symbol }}</div>
                            <span class="badge" :class="p.side === 'Long' ? 'up' : 'down'">
                                {{ p.side === 'Long' ? 'LONG' : 'SHORT' }}
                            </span>
                        </div>
                    </div>

                    <div class="ltr-prices">
                        <div class="ltr-price-col">
                            <div class="ltr-k">Giriş</div>
                            <div class="ltr-v mono">{{ fmt.price(p.averageEntryPrice) }}</div>
                        </div>
                        <div class="ltr-arrow">→</div>
                        <div class="ltr-price-col">
                            <div class="ltr-k">Çıkış</div>
                            <div class="ltr-v mono">{{ exitLabel(p) }}</div>
                        </div>
                    </div>

                    <div class="ltr-pnl" :class="pnlClass(p.realizedPnl)">
                        {{ fmt.moneySigned(p.realizedPnl) }}
                    </div>

                    <div class="ltr-meta">
                        <span class="ltr-duration mono">{{ durationLabel(p) }}</span>
                        <span class="muted tiny">{{ fmt.dateShort(p.closedAt) }}</span>
                    </div>
                </div>
            </transition-group>
        </div>
    `,
};
