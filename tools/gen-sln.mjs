// Tek seferlik: solution items (VS Solution Explorer'da dosya görünürlüğü) eklenmiş yeni sln üretir.
// Çalıştırma: node tools/gen-sln.mjs > BinanceBot.sln
// Mevcut proje GUID'leri korunur; yeni solution folder'lar deterministic "A00000NN-..." GUID'leri alır.
import { readFileSync, readdirSync, statSync, existsSync } from 'node:fs';
import { join, relative } from 'node:path';

const ROOT = process.cwd();
const SLN_FOLDER_GUID = '{2150E333-8FDC-42A3-9474-1A3956D46DE8}';

// Mevcut proje GUID'leri (dokunmuyoruz)
const existing = {
  srcFolder:            '{827E0CD3-B72D-47B6-A68D-7590B98EB39B}',
  backendFolder:        '{FE360695-3F2B-1049-3887-35FBB5135923}',
  domain:               '{C4499F2C-D1EF-4323-868D-2C6754AE2989}',
  application:          '{5B23A188-8571-48C5-8B98-D535EBD68137}',
  infrastructure:       '{5FC033AB-068B-49F9-A77B-3D17493EC0AC}',
  api:                  '{C857F05B-DF61-4B70-8417-78DE9E2F72FA}',
  tests:                '{FF82C35E-C4DB-4B69-B745-40F30A10BE81}',
  toolsFolder:          '{07C2787E-EAC7-C090-1BA3-A61EC2A24D84}',
  mcpAgentBus:          '{362587C9-0992-442E-9111-39CDA636CACD}',
};

// Yeni solution folder GUID'leri — deterministic, tekrar-çalıştırılabilir
let g = 1;
const newGuid = () => {
  const hex = (g++).toString(16).toUpperCase().padStart(2, '0');
  return `{A00000${hex}-0000-0000-0000-000000000001}`;
};

const ROOT_F       = newGuid();
const CLAUDE_F     = newGuid();
const AGENTS_F     = newGuid();
const HOOKS_F      = newGuid();
const SKILLS_F     = newGuid();
const SKILLS_PM    = newGuid();
const SKILLS_ARC   = newGuid();
const SKILLS_BE    = newGuid();
const SKILLS_FE    = newGuid();
const SKILLS_BIN   = newGuid();
const SKILLS_REV   = newGuid();
const SKILLS_TST   = newGuid();
const SKILLS_GLB   = newGuid();
const DOCS_F       = newGuid();
const DOCS_SRC_F   = newGuid();
const AITRACE_F    = newGuid();
const GITHUB_F     = newGuid();

// Disk'teki dosyalar — klasör bazlı listeleme
const winSep = (p) => p.replaceAll('/', '\\');
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
    .filter(p => existsSync(join(ROOT, p)))
    .sort();
};

// Solution item grupları
const groups = [
  { guid: ROOT_F, name: '_root',
    files: ['CLAUDE.md', 'README.md', 'LICENSE', '.gitignore', 'BinanceBot.sln'].filter(f => existsSync(join(ROOT, f))),
    parent: null },
  { guid: CLAUDE_F, name: '.claude',
    files: ['.claude/mcp.json', '.claude/settings.json'].filter(f => existsSync(join(ROOT, f))),
    parent: null },
  { guid: AGENTS_F, name: 'agents', files: listFiles('.claude/agents', p => p.endsWith('.md')),
    parent: CLAUDE_F },
  { guid: HOOKS_F, name: 'hooks', files: listFiles('.claude/hooks'),
    parent: CLAUDE_F },
  { guid: SKILLS_F, name: 'skills', files: [], parent: CLAUDE_F },
  { guid: SKILLS_PM,  name: 'pm',        files: skillFiles('pm'),        parent: SKILLS_F },
  { guid: SKILLS_ARC, name: 'architect', files: skillFiles('architect'), parent: SKILLS_F },
  { guid: SKILLS_BE,  name: 'backend',   files: skillFiles('backend'),   parent: SKILLS_F },
  { guid: SKILLS_FE,  name: 'frontend',  files: skillFiles('frontend'),  parent: SKILLS_F },
  { guid: SKILLS_BIN, name: 'binance',   files: skillFiles('binance'),   parent: SKILLS_F },
  { guid: SKILLS_REV, name: 'reviewer',  files: skillFiles('reviewer'),  parent: SKILLS_F },
  { guid: SKILLS_TST, name: 'tester',    files: skillFiles('tester'),    parent: SKILLS_F },
  { guid: SKILLS_GLB, name: 'global',    files: skillFiles('global'),    parent: SKILLS_F },
  { guid: DOCS_F, name: 'docs',
    files: ['docs/CLAUDE.md', 'docs/glossary.md', 'docs/workspace-guide.md'].filter(f => existsSync(join(ROOT, f))),
    parent: null },
  { guid: DOCS_SRC_F, name: 'sources',
    files: listFiles('docs/sources'), parent: DOCS_F },
  { guid: AITRACE_F, name: '.ai-trace',
    files: [
      '.ai-trace/.gitignore', '.ai-trace/README.md', '.ai-trace/decisions.jsonl',
      '.ai-trace/handoffs.jsonl', '.ai-trace/task-state.json', '.ai-trace/user-notes.jsonl'
    ].filter(f => existsSync(join(ROOT, f))),
    parent: null },
  { guid: GITHUB_F, name: '.github',
    files: listFiles('.github', p => p.endsWith('.yml') || p.endsWith('.yaml') || p.endsWith('.md')),
    parent: null },
];

// SLN çıktısı
const out = [];
out.push('');
out.push('Microsoft Visual Studio Solution File, Format Version 12.00');
out.push('# Visual Studio Version 17');
out.push('VisualStudioVersion = 17.0.31903.59');
out.push('MinimumVisualStudioVersion = 10.0.40219.1');

// Mevcut src hiyerarşisi + Backend projeleri
out.push(`Project("${SLN_FOLDER_GUID}") = "src", "src", "${existing.srcFolder}"`);
out.push('EndProject');
out.push(`Project("${SLN_FOLDER_GUID}") = "Backend", "Backend", "${existing.backendFolder}"`);
out.push('EndProject');
for (const [name, guid] of [
  ['BinanceBot.Domain',         existing.domain],
  ['BinanceBot.Application',    existing.application],
  ['BinanceBot.Infrastructure', existing.infrastructure],
  ['BinanceBot.Api',            existing.api],
  ['BinanceBot.Tests',          existing.tests],
]) {
  const path = `src\\Backend\\${name.replace('BinanceBot.', '')}\\${name}.csproj`;
  out.push(`Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "${name}", "${path}", "${guid}"`);
  out.push('EndProject');
}

// tools hiyerarşisi
out.push(`Project("${SLN_FOLDER_GUID}") = "tools", "tools", "${existing.toolsFolder}"`);
out.push('EndProject');
out.push(`Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "mcp-agent-bus", "tools\\mcp-agent-bus\\mcp-agent-bus.csproj", "${existing.mcpAgentBus}"`);
out.push('EndProject');

// Yeni solution folder'lar (items ile)
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

// Global section
out.push('Global');
out.push('\tGlobalSection(SolutionConfigurationPlatforms) = preSolution');
for (const c of ['Debug|Any CPU', 'Debug|x64', 'Debug|x86', 'Release|Any CPU', 'Release|x64', 'Release|x86']) {
  out.push(`\t\t${c} = ${c}`);
}
out.push('\tEndGlobalSection');

// ProjectConfigurationPlatforms — sadece csproj'lar
out.push('\tGlobalSection(ProjectConfigurationPlatforms) = postSolution');
const csprojGuids = [existing.domain, existing.application, existing.infrastructure, existing.api, existing.tests, existing.mcpAgentBus];
for (const guid of csprojGuids) {
  for (const cfg of ['Debug|Any CPU', 'Debug|x64', 'Debug|x86', 'Release|Any CPU', 'Release|x64', 'Release|x86']) {
    const isAnyCpu = cfg.endsWith('Any CPU');
    const target = isAnyCpu ? cfg : cfg.replace(/\|(x64|x86)/, '|Any CPU');
    out.push(`\t\t${guid}.${cfg}.ActiveCfg = ${target}`);
    out.push(`\t\t${guid}.${cfg}.Build.0 = ${target}`);
  }
}
out.push('\tEndGlobalSection');

out.push('\tGlobalSection(SolutionProperties) = preSolution');
out.push('\t\tHideSolutionNode = FALSE');
out.push('\tEndGlobalSection');

// NestedProjects — mevcut + yeni
out.push('\tGlobalSection(NestedProjects) = preSolution');
// Mevcut (src/Backend/*.csproj altında)
out.push(`\t\t${existing.backendFolder} = ${existing.srcFolder}`);
for (const guid of [existing.domain, existing.application, existing.infrastructure, existing.api, existing.tests]) {
  out.push(`\t\t${guid} = ${existing.backendFolder}`);
}
out.push(`\t\t${existing.mcpAgentBus} = ${existing.toolsFolder}`);
// Yeni solution folder nesting
for (const grp of groups) {
  if (grp.parent) {
    out.push(`\t\t${grp.guid} = ${grp.parent}`);
  }
}
out.push('\tEndGlobalSection');

out.push('EndGlobal');
out.push('');

process.stdout.write(out.join('\r\n'));
