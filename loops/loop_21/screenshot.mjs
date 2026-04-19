import { readdirSync, statSync, existsSync, mkdirSync } from "node:fs";
import { join } from "node:path";
import { pathToFileURL } from "node:url";

const cacheDir = "C:/Users/iyasi/AppData/Local/npm-cache/_npx";
const dirs = readdirSync(cacheDir)
    .map(d => join(cacheDir, d, "node_modules", "playwright"))
    .filter(p => { try { return statSync(p).isDirectory(); } catch { return false; } });
const { chromium } = await import(pathToFileURL(join(dirs[0], "index.mjs")).href);

const pw = "C:/Users/iyasi/AppData/Local/ms-playwright";
const candidates = [
    `${pw}/chromium-1208/chrome-win64/chrome.exe`,
    `${pw}/chromium-1208/chrome-win/chrome.exe`,
];
let exe;
for (const c of candidates) { if (existsSync(c)) { exe = c; break; } }

const outDir = "D:/repos/BinanceBot/loops/loop_21/screenshots";
mkdirSync(outDir, { recursive: true });

const browser = await chromium.launch({ headless: true, executablePath: exe });
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await context.newPage();

const shots = [
    { path: "/index.html",      name: "01-dashboard.png" },
    { path: "/risk.html",       name: "02-risk.png" },
    { path: "/logs.html",       name: "03-logs.png" },
    { path: "/positions.html",  name: "04-positions.png" },
    { path: "/strategies.html", name: "05-strategies.png" },
    { path: "/klines.html",     name: "06-klines.png" },
    { path: "/orderbook.html",  name: "07-orderbook.png" },
    { path: "/orders.html",     name: "08-orders.png" },
];

for (const s of shots) {
    await page.goto(`http://localhost:5188${s.path}`, { waitUntil: "networkidle", timeout: 20000 });
    await page.waitForTimeout(2000);
    await page.screenshot({ path: join(outDir, s.name), fullPage: true });
    console.log(`saved ${s.name}`);
}

await browser.close();
