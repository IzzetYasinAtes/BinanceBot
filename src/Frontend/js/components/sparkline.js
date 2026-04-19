// Sparkline — küçük SVG line+area.
// Prop: points = array of numbers (chronological). trend = 'good'|'bad'|'neutral'
// SVG viewBox 0..W, 0..H; path koordinatları normalize edilir.

import { computed } from "vue";

const W = 100;
const H = 32;

function build(points) {
    const arr = Array.isArray(points) ? points.filter(p => Number.isFinite(Number(p))).map(Number) : [];
    if (arr.length < 2) return { line: "", area: "" };

    const min = Math.min(...arr);
    const max = Math.max(...arr);
    const span = Math.max(1e-9, max - min);
    const stepX = W / (arr.length - 1);

    const xy = arr.map((v, i) => {
        const x = i * stepX;
        const y = H - ((v - min) / span) * H;
        return [x, y];
    });

    const line = xy.map(([x, y], i) =>
        (i === 0 ? "M" : "L") + x.toFixed(2) + "," + y.toFixed(2)
    ).join(" ");

    const area = line + ` L ${W.toFixed(2)},${H.toFixed(2)} L 0,${H.toFixed(2)} Z`;

    return { line, area };
}

export const Sparkline = {
    name: "Sparkline",
    props: {
        points: { type: Array, default: () => [] },
        trend:  { type: String, default: "neutral" }, // good | bad | neutral
    },
    setup(props) {
        const paths = computed(() => build(props.points));
        const trendClass = computed(() => `spark ${props.trend}`);
        return { paths, trendClass, W, H };
    },
    template: `
        <svg :class="trendClass" :viewBox="'0 0 ' + W + ' ' + H" preserveAspectRatio="none">
            <path class="area" :d="paths.area" />
            <path class="line" :d="paths.line" />
        </svg>
    `,
};
