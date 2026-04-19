/**
 * Worker drift check — mirror of `tests/Homespun.Tests/Features/Observability/
 * TraceDictionaryTests.cs` + the web test. Asserts every tracer / logger /
 * span name used under `src/Homespun.Worker/src/` appears in
 * `docs/traces/dictionary.md`, and that every H3 entry under
 * `## Worker-originated traces` has a matching emit site.
 */

import { describe, it, expect } from 'vitest';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { fileURLToPath } from 'node:url';

const currentFile = fileURLToPath(import.meta.url);
const currentDir = path.dirname(currentFile);

function findRepoRoot(start: string): string {
  let dir = start;
  for (;;) {
    if (fs.existsSync(path.join(dir, 'docs', 'traces', 'dictionary.md'))) {
      return dir;
    }
    const parent = path.dirname(dir);
    if (parent === dir) {
      throw new Error(
        `docs/traces/dictionary.md not found walking up parents of ${start}`,
      );
    }
    dir = parent;
  }
}

const REPO_ROOT = findRepoRoot(currentDir);
const DICTIONARY_PATH = path.join(REPO_ROOT, 'docs', 'traces', 'dictionary.md');
const WORKER_SRC_ROOT = path.join(REPO_ROOT, 'src', 'Homespun.Worker', 'src');
const TIER_HEADER = 'Worker-originated traces';

/**
 * Files whose span-name argument is a template literal or other non-literal
 * expression. Each entry MUST carry a comment naming the emitter and the
 * canonical shape documented.
 */
const NonLiteralSpanAllowlist = new Set<string>([
  // (empty — worker currently emits no spans; the surface is carried by
  //  the sibling `worker-spans` change. Add entries here with justification
  //  when template-literal span names are introduced.)
]);

/**
 * Dictionary H3 entries that have no literal emit site — placeholders for
 * dynamic span names. Each entry MUST carry a comment explaining why.
 */
const OrphanExemptSpans = new Set<string>([
  // (empty — add entries as worker spans are introduced with dynamic names.)
]);

interface ParsedDictionary {
  registryNames: Set<string>;
  workerTierH3Names: Set<string>;
}

function parseDictionary(dictPath: string): ParsedDictionary {
  const lines = fs.readFileSync(dictPath, 'utf8').split(/\r?\n/);
  const registry = new Set<string>();
  const workerH3 = new Set<string>();
  const tierSections = new Set([
    'Client-originated traces',
    'Server-originated traces',
    'Worker-originated traces',
  ]);

  let currentH2: string | null = null;
  let inRegistryTable = false;
  const backtickRe = /`([^`]+)`/;

  for (const raw of lines) {
    const line = raw.replace(/\r$/, '');

    if (line.startsWith('## ')) {
      currentH2 = line.slice(3).trim();
      inRegistryTable = currentH2 === 'Tracer / ActivitySource registry';
      continue;
    }

    if (inRegistryTable && line.startsWith('|')) {
      const stripped = line.replace(/^\||\|$/g, '').trim();
      if (stripped.startsWith('Name')) continue;
      if (/^[\s\-:|]+$/.test(stripped)) continue;
      const cells = line.replace(/^\||\|$/g, '').split('|');
      if (cells.length === 0) continue;
      const match = backtickRe.exec(cells[0] ?? '');
      if (match) registry.add(match[1].trim());
      continue;
    }

    if (
      line.startsWith('### ') &&
      currentH2 !== null &&
      tierSections.has(currentH2) &&
      currentH2 === TIER_HEADER
    ) {
      const headerText = line.slice(4).trim();
      const m = backtickRe.exec(headerText);
      if (!m) continue;
      workerH3.add(m[1].trim());
    }
  }

  return { registryNames: registry, workerTierH3Names: workerH3 };
}

interface SpanSite {
  name: string;
  relativePath: string;
  line: number;
}

interface DynamicSpanSite {
  relativePath: string;
  line: number;
}

interface ScanResult {
  tracerNames: Set<string>;
  loggerNames: Set<string>;
  literalSpans: SpanSite[];
  dynamicSites: DynamicSpanSite[];
}

function walkSources(root: string): string[] {
  const out: string[] = [];
  const skipDirs = new Set(['node_modules', 'test']);

  function recurse(dir: string) {
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (skipDirs.has(entry.name)) continue;
        recurse(full);
        continue;
      }
      if (!entry.isFile()) continue;
      if (!/\.ts$/.test(entry.name)) continue;
      if (/\.(test|spec)\.ts$/.test(entry.name)) continue;
      out.push(full);
    }
  }

  recurse(root);
  return out;
}

const CONST_STRING_RE =
  /\bconst\s+(?<name>\w+)(?:\s*:\s*[^=]+)?\s*=\s*(['"])(?<value>[^'"]+)\2/g;

function collectConsts(text: string): Map<string, string> {
  const out = new Map<string, string>();
  for (const m of text.matchAll(CONST_STRING_RE)) {
    const name = m.groups?.name;
    const value = m.groups?.value;
    if (name && value !== undefined) out.set(name, value);
  }
  return out;
}

function buildCallRegex(fn: string): RegExp {
  return new RegExp(
    String.raw`(?:\.|\b)` +
      fn +
      String.raw`\s*\(\s*(?<arg>'[^']*'|"[^"]*"|` +
      '`[^`]*`' +
      String.raw`|[A-Za-z_][\w\.]*)`,
    'g',
  );
}

const GET_TRACER_RE = buildCallRegex('getTracer');
const GET_LOGGER_RE = buildCallRegex('getLogger');
const START_SPAN_RE = buildCallRegex('startSpan');
const START_ACTIVE_SPAN_RE = buildCallRegex('startActiveSpan');

function resolveName(
  arg: string,
  fileConsts: Map<string, string>,
  globalConsts: Map<string, string>,
): { literal: string | null; dynamic: boolean } {
  if (arg.length >= 2 && (arg[0] === "'" || arg[0] === '"')) {
    return { literal: arg.slice(1, -1), dynamic: false };
  }
  if (arg.startsWith('`')) {
    if (arg.includes('${')) return { literal: null, dynamic: true };
    return { literal: arg.slice(1, -1), dynamic: false };
  }
  const viaFile = fileConsts.get(arg);
  if (viaFile !== undefined) return { literal: viaFile, dynamic: false };
  const viaGlobal = globalConsts.get(arg);
  if (viaGlobal !== undefined) return { literal: viaGlobal, dynamic: false };
  return { literal: null, dynamic: true };
}

function scanWorkerSources(): ScanResult {
  const files = walkSources(WORKER_SRC_ROOT);
  const texts = new Map<string, string>();
  const constsByFile = new Map<string, Map<string, string>>();
  const globalConsts = new Map<string, string>();

  for (const file of files) {
    const text = fs.readFileSync(file, 'utf8');
    texts.set(file, text);
    const c = collectConsts(text);
    constsByFile.set(file, c);
    for (const [k, v] of c) {
      if (!globalConsts.has(k)) globalConsts.set(k, v);
    }
  }

  const tracerNames = new Set<string>();
  const loggerNames = new Set<string>();
  const literalSpans: SpanSite[] = [];
  const dynamicSites: DynamicSpanSite[] = [];

  for (const file of files) {
    const text = texts.get(file)!;
    const fileConsts = constsByFile.get(file)!;
    const rel = path.relative(REPO_ROOT, file).replace(/\\/g, '/');
    const lines = text.split(/\r?\n/);

    for (const m of text.matchAll(GET_TRACER_RE)) {
      const arg = m.groups?.arg;
      if (!arg) continue;
      const { literal } = resolveName(arg, fileConsts, globalConsts);
      if (literal !== null) tracerNames.add(literal);
    }
    for (const m of text.matchAll(GET_LOGGER_RE)) {
      const arg = m.groups?.arg;
      if (!arg) continue;
      const { literal } = resolveName(arg, fileConsts, globalConsts);
      if (literal !== null) loggerNames.add(literal);
    }

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i];
      for (const re of [START_SPAN_RE, START_ACTIVE_SPAN_RE]) {
        re.lastIndex = 0;
        let m: RegExpExecArray | null;
        while ((m = re.exec(line)) !== null) {
          const arg = m.groups?.arg;
          if (!arg) continue;
          const resolved = resolveName(arg, fileConsts, globalConsts);
          if (resolved.literal !== null) {
            literalSpans.push({
              name: resolved.literal,
              relativePath: rel,
              line: i + 1,
            });
          } else if (resolved.dynamic) {
            dynamicSites.push({ relativePath: rel, line: i + 1 });
          }
        }
      }
    }
  }

  return { tracerNames, loggerNames, literalSpans, dynamicSites };
}

describe('trace dictionary — worker', () => {
  it('dictionary file exists', () => {
    expect(fs.existsSync(DICTIONARY_PATH)).toBe(true);
  });

  it('every tracer and logger name is registered in the dictionary', () => {
    const dict = parseDictionary(DICTIONARY_PATH);
    const scan = scanWorkerSources();
    const combined = new Set([...scan.tracerNames, ...scan.loggerNames]);
    const missing = [...combined]
      .filter((n) => !dict.registryNames.has(n))
      .sort();

    expect(
      missing,
      missing.length > 0
        ? `Tracer/logger names in src/Homespun.Worker/src not in docs/traces/dictionary.md registry: ${missing.join(', ')}. Add them to the Tracer / ActivitySource registry table.`
        : 'ok',
    ).toEqual([]);
  });

  it('every literal span name is documented under Worker-originated traces', () => {
    const dict = parseDictionary(DICTIONARY_PATH);
    const scan = scanWorkerSources();
    const undocumented = scan.literalSpans
      .filter((s) => !dict.workerTierH3Names.has(s.name))
      .sort((a, b) => a.name.localeCompare(b.name));

    if (undocumented.length === 0) {
      expect(undocumented).toEqual([]);
      return;
    }

    const detail = undocumented
      .map(
        (s) =>
          `  - Span \`${s.name}\` used in ${s.relativePath}:${s.line} is not documented in docs/traces/dictionary.md`,
      )
      .join('\n');
    throw new Error(
      `Undocumented span name(s) emitted by src/Homespun.Worker/src:\n${detail}\n` +
        `Add an H3 entry under '## Worker-originated traces' in docs/traces/dictionary.md.`,
    );
  });

  it('dynamic span names are allowlisted with justification', () => {
    const scan = scanWorkerSources();
    const offenders = scan.dynamicSites
      .filter((s) => !NonLiteralSpanAllowlist.has(s.relativePath))
      .sort((a, b) => a.relativePath.localeCompare(b.relativePath));

    if (offenders.length === 0) {
      expect(offenders).toEqual([]);
      return;
    }

    const detail = offenders
      .map((s) => `  - Dynamic span at ${s.relativePath}:${s.line}`)
      .join('\n');
    throw new Error(
      `Non-literal (template / variable) span name(s) in src/Homespun.Worker/src:\n${detail}\n` +
        `Add the file to NonLiteralSpanAllowlist in this test with a justifying comment, ` +
        `and document the canonical shape under '## Worker-originated traces' in docs/traces/dictionary.md.`,
    );
  });

  it('no orphan H3 entries under Worker-originated traces', () => {
    const dict = parseDictionary(DICTIONARY_PATH);
    const scan = scanWorkerSources();
    const emitted = new Set(scan.literalSpans.map((s) => s.name));
    const orphans = [...dict.workerTierH3Names]
      .filter((name) => !emitted.has(name) && !OrphanExemptSpans.has(name))
      .sort();

    expect(
      orphans,
      orphans.length > 0
        ? `Dictionary H3 entries under '## Worker-originated traces' with no matching startSpan/startActiveSpan call in src/Homespun.Worker/src: ${orphans.join(', ')}. Remove the entry or add its emit site — or add to OrphanExemptSpans with a justifying comment.`
        : 'ok',
    ).toEqual([]);
  });
});
