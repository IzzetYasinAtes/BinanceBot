// Ring progress meter — SVG dairesel gösterge.
// Prop: value (0-100), size, color override, label.
// Renkler eşiklere göre otomatik: <40 good, <70 warn, >=70 bad.

import { computed } from "vue";

export const RingProgress = {
    template: `
        <div class="ring-progress" :style="{ width: size + 'px', height: size + 'px' }">
            <svg :viewBox="'0 0 ' + size + ' ' + size" :width="size" :height="size">
                <defs>
                    <linearGradient :id="gradId" x1="0" y1="0" x2="1" y2="1">
                        <stop offset="0%" :stop-color="strokeColor" />
                        <stop offset="100%" :stop-color="strokeColor2" />
                    </linearGradient>
                </defs>
                <circle class="ring-bg"
                        :cx="half" :cy="half" :r="radius"
                        :stroke-width="strokeW"
                        fill="none"/>
                <circle class="ring-fg"
                        :cx="half" :cy="half" :r="radius"
                        :stroke-width="strokeW"
                        :stroke="'url(#' + gradId + ')'"
                        :stroke-dasharray="circumference"
                        :stroke-dashoffset="dashOffset"
                        fill="none"
                        :transform="'rotate(-90 ' + half + ' ' + half + ')'" />
            </svg>
            <div class="ring-content">
                <div class="ring-value tabular-nums" :class="valueClass">{{ displayValue }}</div>
                <div class="ring-label" v-if="label">{{ label }}</div>
            </div>
        </div>
    `,
    props: {
        value: { type: Number, default: 0 },          // 0 - 100
        size: { type: Number, default: 120 },
        strokeWidth: { type: Number, default: 10 },
        label: { type: String, default: "" },
        decimals: { type: Number, default: 1 },
        suffix: { type: String, default: "%" },
        // "good" | "warn" | "bad" | "auto" | "indigo"
        tone: { type: String, default: "auto" },
        // eşikler (value cinsinden) — auto mode için.
        warnAt: { type: Number, default: 40 },
        badAt: { type: Number, default: 70 },
    },
    setup(props) {
        const gradId = `ring-grad-${Math.random().toString(36).slice(2, 9)}`;
        const half = computed(() => props.size / 2);
        const strokeW = computed(() => props.strokeWidth);
        const radius = computed(() => half.value - strokeW.value);
        const circumference = computed(() => 2 * Math.PI * radius.value);

        const clamped = computed(() => {
            const v = Number(props.value);
            if (!isFinite(v)) return 0;
            return Math.max(0, Math.min(100, v));
        });

        const dashOffset = computed(() =>
            circumference.value * (1 - clamped.value / 100));

        const effectiveTone = computed(() => {
            if (props.tone !== "auto") return props.tone;
            const v = clamped.value;
            if (v >= props.badAt) return "bad";
            if (v >= props.warnAt) return "warn";
            return "good";
        });

        const palette = {
            good:   ["#10b981", "#34d399"],
            warn:   ["#f59e0b", "#fbbf24"],
            bad:    ["#ef4444", "#f87171"],
            indigo: ["#6366f1", "#06b6d4"],
        };

        const strokeColor = computed(() =>
            (palette[effectiveTone.value] || palette.indigo)[0]);
        const strokeColor2 = computed(() =>
            (palette[effectiveTone.value] || palette.indigo)[1]);

        const valueClass = computed(() => {
            const t = effectiveTone.value;
            if (t === "good") return "metric-good";
            if (t === "warn") return "metric-warn";
            if (t === "bad")  return "metric-bad";
            return "";
        });

        const displayValue = computed(() => {
            const v = Number(props.value);
            if (!isFinite(v)) return "—";
            return `${v.toFixed(props.decimals)}${props.suffix}`;
        });

        return {
            gradId, half, radius, strokeW, circumference, dashOffset,
            strokeColor, strokeColor2, valueClass, displayValue,
        };
    },
};
