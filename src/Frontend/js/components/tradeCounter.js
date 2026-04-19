// TradeCounter — saat-başı işlem hedef vs gerçek göstergesi.
// Prop: actualCount (son 1h closed), targetCount (150), totalClosed.
// Template: animated-number + progress bar + renk (yeşil/amber/kırmızı) + dev mikro özet.

import { computed } from "vue";
import { AnimatedNumber } from "./animatedNumber.js";

export const TradeCounter = {
    name: "TradeCounter",
    components: { AnimatedNumber },
    props: {
        actualCount:  { type: Number, default: 0 },
        targetCount:  { type: Number, default: 150 },
        totalClosed:  { type: Number, default: 0 },
    },
    setup(props) {
        const ratio = computed(() => {
            const t = Math.max(1, Number(props.targetCount) || 1);
            const a = Math.max(0, Number(props.actualCount) || 0);
            return Math.min(1, a / t);
        });

        const ratioPct = computed(() => Math.round(ratio.value * 100));

        const fillClass = computed(() => {
            const r = ratio.value;
            if (r >= 0.80) return "good";
            if (r >= 0.50) return "warn";
            return "bad";
        });

        const stateLabel = computed(() => {
            const r = ratio.value;
            if (r >= 1.0)  return "Hedef aşıldı";
            if (r >= 0.80) return "Hedefe yakın";
            if (r >= 0.50) return "Orta tempo";
            return "Düşük tempo";
        });

        const stateBadge = computed(() => {
            const r = ratio.value;
            if (r >= 0.80) return "good";
            if (r >= 0.50) return "warn";
            return "bad";
        });

        const remaining = computed(() => {
            const t = Number(props.targetCount) || 0;
            const a = Number(props.actualCount) || 0;
            return Math.max(0, t - a);
        });

        return { ratio, ratioPct, fillClass, stateLabel, stateBadge, remaining };
    },
    template: `
        <div class="trade-counter">
            <div class="tc-top">
                <div class="tc-current">
                    <AnimatedNumber :value="actualCount" :decimals="0" :duration-ms="600" />
                    <span class="tc-of">/ {{ targetCount }}</span>
                </div>
                <div class="tc-state">
                    <span class="badge" :class="stateBadge">{{ stateLabel }}</span>
                    <span class="muted tiny">{{ ratioPct }}% · son 1 saat</span>
                </div>
            </div>
            <div class="tc-bar">
                <div class="tc-bar-fill" :class="fillClass" :style="{ width: (ratioPct) + '%' }"></div>
                <div class="tc-bar-target" title="Saatlik hedef"></div>
            </div>
            <div class="tc-foot">
                <span class="muted tiny">
                    Hedef {{ targetCount }} / saat — micro-scalping ritmi
                </span>
                <span class="muted tiny">
                    Toplam kapalı: <span class="mono">{{ totalClosed }}</span>
                    · Kalan: <span class="mono">{{ remaining }}</span>
                </span>
            </div>
        </div>
    `,
};
