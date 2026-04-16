// BinanceBot.sln (classic Visual Studio Solution File) üretir.
// Çalıştırma: node tools/gen-sln.mjs > BinanceBot.sln
//
// Classic .sln formatı; VS 2022'nin her sürümünde rendere edilir.
// Root seviyedeki dosyalar (CLAUDE.md, README.md, LICENSE, .gitignore) VS konvansiyonu
// olan "Solution Items" folder'ı altında gösterilir — VS'nin dünyada milyonlarca
// .NET projesinde kullandığı standart pattern.
import { readdirSync, statSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const ROOT = process.cwd();
const SLN_FOLDER_GUID = '{2150E333-8FDC-42A3-9474-1A3956D46DE8}';
const CSHARP_GUID      = '{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}';

// Mevcut proje GUID'leri (repo geçmişinde sabitlenmiş)
const existing = {
  domain:         '{C4499F2C-D1EF-4323-868D-2C6754AE2989}',
  application:    '{5B23A188-8571-48C5-8B98-D535EBD68137}',
  infrastructure: '{5FC033AB-068B-49F9-A77B-3D17493EC0AC}',
  api:            '{C857F05B-DF61-4B70-8417-78DE9E2F72FA}',
  tests:          '{FF82C35E-C4DB-4B69-B745-40F30A10BE81}',
  mcpAgentBus:    '{362587C9-0992-442E-9111-39CDA636CACD}',
};

// Solution folder GUID'leri — deterministic, her run aynı
let g = 1;
const newGuid = () => `{A00000${(g++).toString(16).toUpperCase().padStart(2,'0')}-0000-0000-0000-000000000001}`;

const SOLUTION_ITEMS_F = newGuid();  // VS konvansiyonu: root dosyalar
const SRC_F            = newGuid();
const TESTS_F          = newGuid();
const TOOLS_F          = newGuid();
const CLAUDE_F         = newGuid();
const AGENTS_F         = newGuid();
const HOOKS_F          = newGuid();
const SKILLS_F         = newGuid();
const SKILLS_PM        = newGuid();
const SKILLS_ARC       = newGuid();
const SKILLS_BE        = newGuid();
const SKILLS_FE        = newGuid();
const SKILLS_BIN       = newGuid();
const SKILLS_REV       = newGuid();
const SKILLS_TST       = newGuid();
const SKILLS_GLB       = newGuid();
const DOCS_F           = newGuid();
const DOCS_SRC_F       = newGuid();
const AITRACE_F        = newGuid();

const winSep = (p) => p.replaceAll('/', '\\');
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
  const dir = join(ROOT, '.claude/skills');
  if (!existsSync(dir)) return [];
  return readdirSync(dir)
    .filter(n => prefix === 'global' ? n === 'tell-pm' : n.startsWith(prefix + '-'))
    .map(n => join('.claude/skills', n, 'SKILL.md'))
    .filter(p => exists(p))
    .sort();
};

// Solution folder grupları (items + parent)
const groups = [
  // Root seviye dosyalar → VS konvansiyonu "Solution Items"
  { guid: SOLUTION_ITEMS_F, name: 'Solution Items',
    files: ['CLAUDE.md', 'README.md', 'LICENSE', '.gitignore', 'BinanceBot.sln', '.mcp.json'].filter(exists),
    parent: null },
  // .claude tree
  { guid: CLAUDE_F, name: '.claude',
    files: ['.claude/settings.json'].filter(exists),
    parent: null },
  { guid: AGENTS_F, name: 'agents',
    files: listFiles('.claude/agents', p => p.endsWith('.md')),
    parent: CLAUDE_F },
  { guid: HOOKS_F, name: 'hooks', files: listFiles('.claude/hooks'), parent: CLAUDE_F },
  { guid: SKILLS_F, name: 'skills', files: [], parent: CLAUDE_F },
  { guid: SKILLS_PM,  name: 'pm',        files: skillFiles('pm'),        parent: SKILLS_F },
  { guid: SKILLS_ARC, name: 'architect', files: skillFiles('architect'), parent: SKILLS_F },
  { guid: SKILLS_BE,  name: 'backend',   files: skillFiles('backend'),   parent: SKILLS_F },
  { guid: SKILLS_FE,  name: 'frontend',  files: skillFiles('frontend'),  parent: SKILLS_F },
  { guid: SKILLS_BIN, name: 'binance',   files: skillFiles('binance'),   parent: SKILLS_F },
  { guid: SKILLS_REV, name: 'reviewer',  files: skillFiles('reviewer'),  parent: SKILLS_F },
  { guid: SKILLS_TST, name: 'tester',    files: skillFiles('tester'),    parent: SKILLS_F },
  { guid: SKILLS_GLB, name: 'global',    files: skillFiles('global'),    parent: SKILLS_F },
  // docs tree
  { guid: DOCS_F, name: 'docs',
    files: ['docs/CLAUDE.md', 'docs/glossary.md', 'docs/workspace-guide.md'].filter(exists),
    parent: null },
  { guid: DOCS_SRC_F, name: 'sources',
    files: listFiles('docs/sources'), parent: DOCS_F },
  // .ai-trace
  { guid: AITRACE_F, name: '.ai-trace',
    files: [
      '.ai-trace/.gitignore', '.ai-trace/README.md', '.ai-trace/decisions.jsonl',
      '.ai-trace/handoffs.jsonl', '.ai-trace/task-state.json', '.ai-trace/user-notes.jsonl'
    ].filter(exists),
    parent: null },
  // NOT: .github solution folder eklenmiyor — VS kendi "GitHub Actions" görünümüyle auto-detect ediyor.
];

// SLN çıktısı
const out = [];
out.push('');
out.push('Microsoft Visual Studio Solution File, Format Version 12.00');
out.push('# Visual Studio Version 17');
out.push('VisualStudioVersion = 17.0.31903.59');
out.push('MinimumVisualStudioVersion = 10.0.40219.1');

// src solution folder + 4 backend projesi (Backend ara klasörü YOK)
out.push(`Project("${SLN_FOLDER_GUID}") = "src", "src", "${SRC_F}"`);
out.push('EndProject');
for (const [name, guid] of [
  ['BinanceBot.Domain',         existing.domain],
  ['BinanceBot.Application',    existing.application],
  ['BinanceBot.Infrastructure', existing.infrastructure],
  ['BinanceBot.Api',            existing.api],
]) {
  const folder = name.replace('BinanceBot.', '');
  const path = `src\\${folder}\\${name}.csproj`;
  out.push(`Project("${CSHARP_GUID}") = "${name}", "${path}", "${guid}"`);
  out.push('EndProject');
}

// tests solution folder + Tests projesi
out.push(`Project("${SLN_FOLDER_GUID}") = "tests", "tests", "${TESTS_F}"`);
out.push('EndProject');
out.push(`Project("${CSHARP_GUID}") = "BinanceBot.Tests", "tests\\Tests\\BinanceBot.Tests.csproj", "${existing.tests}"`);
out.push('EndProject');

// tools solution folder + mcp-agent-bus
out.push(`Project("${SLN_FOLDER_GUID}") = "tools", "tools", "${TOOLS_F}"`);
out.push('EndProject');
out.push(`Project("${CSHARP_GUID}") = "mcp-agent-bus", "tools\\mcp-agent-bus\\mcp-agent-bus.csproj", "${existing.mcpAgentBus}"`);
out.push('EndProject');

// Diğer solution folder'lar (items ile)
for (const grp of groups) {
  out.push(`Project("${SLN_FOLDER_GUID}") = "${grp.name}", "${grp.name}", "${grp.guid}"`);
  if (grp.files.length > 0) {
    out.push('\tProjectSection(SolutionItems) = preProject');
    for (const f of grp.files) {
      const w = winSep(f);
      out.push(`\t\t${w} = ${w}`);
    }
    out.push('\tEndProjectSection');
  }
  out.push('EndProject');
}

// Global bölümü
out.push('Global');
out.push('\tGlobalSection(SolutionConfigurationPlatforms) = preSolution');
for (const c of ['Debug|Any CPU', 'Debug|x64', 'Debug|x86', 'Release|Any CPU', 'Release|x64', 'Release|x86']) {
  out.push(`\t\t${c} = ${c}`);
}
out.push('\tEndGlobalSection');

out.push('\tGlobalSection(ProjectConfigurationPlatforms) = postSolution');
const csprojGuids = [existing.domain, existing.application, existing.infrastructure, existing.api, existing.tests, existing.mcpAgentBus];
for (const guid of csprojGuids) {
  for (const cfg of ['Debug|Any CPU', 'Debug|x64', 'Debug|x86', 'Release|Any CPU', 'Release|x64', 'Release|x86']) {
    const target = cfg.endsWith('Any CPU') ? cfg : cfg.replace(/\|(x64|x86)/, '|Any CPU');
    out.push(`\t\t${guid}.${cfg}.ActiveCfg = ${target}`);
    out.push(`\t\t${guid}.${cfg}.Build.0 = ${target}`);
  }
}
out.push('\tEndGlobalSection');

out.push('\tGlobalSection(SolutionProperties) = preSolution');
out.push('\t\tHideSolutionNode = FALSE');
out.push('\tEndGlobalSection');

// NestedProjects
out.push('\tGlobalSection(NestedProjects) = preSolution');
// csproj'lar kendi solution folder'larına
out.push(`\t\t${existing.domain} = ${SRC_F}`);
out.push(`\t\t${existing.application} = ${SRC_F}`);
out.push(`\t\t${existing.infrastructure} = ${SRC_F}`);
out.push(`\t\t${existing.api} = ${SRC_F}`);
out.push(`\t\t${existing.tests} = ${TESTS_F}`);
out.push(`\t\t${existing.mcpAgentBus} = ${TOOLS_F}`);
// Solution folder nesting
for (const grp of groups) {
  if (grp.parent) out.push(`\t\t${grp.guid} = ${grp.parent}`);
}
out.push('\tEndGlobalSection');

out.push('EndGlobal');
out.push('');

process.stdout.write(out.join('\r\n'));
