// Tek seferlik: solution items + klasör hiyerarşisi dolu yeni .slnx üretir.
// Çalıştırma: node tools/gen-sln.mjs > BinanceBot.slnx
//
// .slnx (VS 2022 17.10+) classic .sln'den farklı olarak "root-level loose items"
// destekler — CLAUDE.md, README.md vb. klasör wrapper'ı olmadan Solution
// Explorer'ın Solution node'u altında doğrudan görünür.
import { readdirSync, statSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const ROOT = process.cwd();
const winSep = (p) => p.replaceAll('/', '\\');
const esc = (s) => s.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');
const exists = (p) => existsSync(join(ROOT, p));
const listFiles = (dir, filter = () => true) => {
  const abs = join(ROOT, dir);
  if (!existsSync(abs)) return [];
  return readdirSync(abs)
    .filter(n => !n.startsWith('.DS_Store'))
    .map(n => join(dir, n))
    .filter(p => statSync(join(ROOT, p)).isFile() && filter(p))
    .sort();
};
const skillFiles = (prefix) => {
  const skillsDir = join(ROOT, '.claude/skills');
  if (!existsSync(skillsDir)) return [];
  return readdirSync(skillsDir)
    .filter(n => prefix === 'global' ? n === 'tell-pm' : n.startsWith(prefix + '-'))
    .map(n => join('.claude/skills', n, 'SKILL.md'))
    .filter(p => exists(p))
    .sort();
};

const lines = [];
lines.push('<Solution>');

// 1) Root seviye loose dosyalar (VS Solution Explorer'da Solution node'un altında doğrudan görünür)
for (const f of ['CLAUDE.md', 'README.md', 'LICENSE', '.gitignore']) {
  if (exists(f)) lines.push(`  <File Path="${esc(winSep(f))}" />`);
}

// 2) src/ (Clean Arch canonical — Backend intermediate YOK)
lines.push('  <Folder Name="/src/">');
for (const proj of ['Domain', 'Application', 'Infrastructure', 'Api']) {
  const path = winSep(`src/${proj}/BinanceBot.${proj}.csproj`);
  if (exists(path)) lines.push(`    <Project Path="${esc(path)}" />`);
}
if (exists('src/CLAUDE.md')) lines.push(`    <File Path="${esc(winSep('src/CLAUDE.md'))}" />`);
if (exists('src/Frontend/CLAUDE.md')) {
  lines.push('    <Folder Name="/src/Frontend/">');
  lines.push(`      <File Path="${esc(winSep('src/Frontend/CLAUDE.md'))}" />`);
  lines.push('    </Folder>');
}
lines.push('  </Folder>');

// 3) tests/
lines.push('  <Folder Name="/tests/">');
if (exists('tests/Tests/BinanceBot.Tests.csproj')) {
  lines.push(`    <Project Path="${esc(winSep('tests/Tests/BinanceBot.Tests.csproj'))}" />`);
}
lines.push('  </Folder>');

// 4) tools/
lines.push('  <Folder Name="/tools/">');
if (exists('tools/mcp-agent-bus/mcp-agent-bus.csproj')) {
  lines.push(`    <Project Path="${esc(winSep('tools/mcp-agent-bus/mcp-agent-bus.csproj'))}" />`);
}
if (exists('tools/mcp-agent-bus/CLAUDE.md')) {
  lines.push('    <Folder Name="/tools/mcp-agent-bus/">');
  lines.push(`      <File Path="${esc(winSep('tools/mcp-agent-bus/CLAUDE.md'))}" />`);
  if (exists('tools/mcp-agent-bus/README.md')) {
    lines.push(`      <File Path="${esc(winSep('tools/mcp-agent-bus/README.md'))}" />`);
  }
  lines.push('    </Folder>');
}
if (exists('tools/gen-sln.mjs')) {
  lines.push(`    <File Path="${esc(winSep('tools/gen-sln.mjs'))}" />`);
}
lines.push('  </Folder>');

// 5) .claude/
lines.push('  <Folder Name="/.claude/">');
for (const f of ['.claude/mcp.json', '.claude/settings.json']) {
  if (exists(f)) lines.push(`    <File Path="${esc(winSep(f))}" />`);
}
lines.push('  </Folder>');

// .claude/agents/
const agentsFiles = listFiles('.claude/agents', p => p.endsWith('.md'));
if (agentsFiles.length) {
  lines.push('  <Folder Name="/.claude/agents/">');
  for (const f of agentsFiles) lines.push(`    <File Path="${esc(winSep(f))}" />`);
  lines.push('  </Folder>');
}

// .claude/hooks/
const hooksFiles = listFiles('.claude/hooks');
if (hooksFiles.length) {
  lines.push('  <Folder Name="/.claude/hooks/">');
  for (const f of hooksFiles) lines.push(`    <File Path="${esc(winSep(f))}" />`);
  lines.push('  </Folder>');
}

// .claude/skills/ (empty container)
lines.push('  <Folder Name="/.claude/skills/" />');

// Skill alt klasörleri (agent başına)
const skillGroups = [
  ['pm', 'pm'],
  ['architect', 'architect'],
  ['backend', 'backend'],
  ['frontend', 'frontend'],
  ['binance', 'binance'],
  ['reviewer', 'reviewer'],
  ['tester', 'tester'],
  ['global', 'global'],
];
for (const [prefix, label] of skillGroups) {
  const files = skillFiles(prefix);
  if (files.length === 0) continue;
  lines.push(`  <Folder Name="/.claude/skills/${label}/">`);
  for (const f of files) lines.push(`    <File Path="${esc(winSep(f))}" />`);
  lines.push('  </Folder>');
}

// 6) docs/
lines.push('  <Folder Name="/docs/">');
for (const f of ['docs/CLAUDE.md', 'docs/glossary.md', 'docs/workspace-guide.md']) {
  if (exists(f)) lines.push(`    <File Path="${esc(winSep(f))}" />`);
}
lines.push('  </Folder>');

// docs/sources/
const sourcesFiles = listFiles('docs/sources');
if (sourcesFiles.length) {
  lines.push('  <Folder Name="/docs/sources/">');
  for (const f of sourcesFiles) lines.push(`    <File Path="${esc(winSep(f))}" />`);
  lines.push('  </Folder>');
}

// 7) .ai-trace/
const aiFiles = [
  '.ai-trace/.gitignore', '.ai-trace/README.md', '.ai-trace/decisions.jsonl',
  '.ai-trace/handoffs.jsonl', '.ai-trace/task-state.json', '.ai-trace/user-notes.jsonl'
].filter(exists);
if (aiFiles.length) {
  lines.push('  <Folder Name="/.ai-trace/">');
  for (const f of aiFiles) lines.push(`    <File Path="${esc(winSep(f))}" />`);
  lines.push('  </Folder>');
}

// 8) .github/
const ghFiles = listFiles('.github/workflows', p => /\.(yml|yaml)$/.test(p));
if (ghFiles.length || exists('.github')) {
  lines.push('  <Folder Name="/.github/" />');
  if (ghFiles.length) {
    lines.push('  <Folder Name="/.github/workflows/">');
    for (const f of ghFiles) lines.push(`    <File Path="${esc(winSep(f))}" />`);
    lines.push('  </Folder>');
  }
}

lines.push('</Solution>');
lines.push('');
process.stdout.write(lines.join('\r\n'));
