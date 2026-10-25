// Run from the repository root with Node.js 22+; no dependencies.
import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
const read = p => fs.readFileSync(p, 'utf8');
const walk = dir => fs.readdirSync(dir, { withFileTypes: true }).flatMap(e =>
  e.isDirectory() ? walk(path.join(dir, e.name)) : [path.join(dir, e.name)]);
const problems = [];
const check = (ok, message) => { if (!ok) problems.push(message); };

// Balance C# blocks, ignoring comments and string/character literals.
function block(source, start) {
  let depth = 0;
  for (let i = start; i < source.length; i++) {
    if (source.startsWith('//', i)) { i = source.indexOf('\n', i); if (i < 0) break; }
    else if (source.startsWith('/*', i)) { i = source.indexOf('*/', i + 2) + 1; }
    else if (source[i] === '"' || source[i] === "'") {
      const quote = source[i], verbatim = source[i - 1] === '@';
      for (i++; i < source.length; i++) {
        if (source[i] === '\\' && !verbatim) i++;
        else if (source[i] === quote) {
          if (verbatim && source[i + 1] === '"') i++;
          else break;
        }
      }
    } else if (source[i] === '{') depth++;
    else if (source[i] === '}' && --depth === 0) return i;
  }
  throw new Error('Unbalanced C# block');
}
const classes = new Map(), commandSettings = new Map();
for (const file of walk('src/Packata.Cli').filter(p => p.endsWith('.cs') && !/[\\/](obj|bin)[\\/]/.test(p))) {
  const source = read(file);
  for (const m of source.matchAll(/\bclass\s+(\w+)\s*:\s*(\w+)(?:<(\w+)>)?/g)) {
    const start = source.indexOf('{', m.index);
    classes.set(m[1], { parent: m[2], body: source.slice(start, block(source, start) + 1) });
    if (/^(Async)?Command$/.test(m[2])) commandSettings.set(m[1], m[3]);
  }
}
function settings(name) {
  if (name === 'CommandSettings') return [];
  const entry = classes.get(name);
  assert(entry, `Unknown settings class ${name}; update the source reader`);
  return [...settings(entry.parent), ...[...entry.body.matchAll(/\[Command(?:Option|Argument)\((?:\d+,\s*)?"([^"]+)"\)\]/g)].map(m => m[1])];
}
const commands = new Map();
function registrations(source, prefix = '') {
  const re = /\.Add(Branch|Command<(\w+)>)\("([^"]+)"/g;
  let m;
  while ((m = re.exec(source))) {
    const name = (prefix + ' ' + m[3]).trim();
    if (m[1] === 'Branch') {
      const start = source.indexOf('{', re.lastIndex), end = block(source, start);
      registrations(source.slice(start + 1, end), name); re.lastIndex = end + 1;
    } else {
      assert(commandSettings.has(m[2]), `Unknown command ${m[2]}`);
      commands.set(name, settings(commandSettings.get(m[2])));
    }
  }
}
registrations(read('src/Packata.Cli/Program.cs'));
assert(commands.size > 0, 'No CLI registrations discovered');
const documented = new Map();
for (const file of walk('docs').filter(p => p.endsWith('.md') && !/[\\/](_site|vendor)[\\/]/.test(p))) {
  const source = read(file);
  if (!source.startsWith('---')) continue;
  for (const m of source.matchAll(/((?:<!-- cli:[^>]+ -->\s*)+)([\s\S]*?)(?=<!-- (?:cli:|end-cli)|$)/g)) {
    for (const tag of m[1].matchAll(/<!-- cli:(.*?) -->/g)) {
      check(!documented.has(tag[1]), `Duplicate documentation for ${tag[1]}`);
      documented.set(tag[1], { file, body: m[2] });
    }
  }
}
let optionCount = 0;
for (const [name, attributes] of commands) {
  const doc = documented.get(name);
  check(doc, `Missing command documentation: ${name}`);
  if (!doc) continue;
  for (const attribute of attributes) {
    const tokens = attribute.startsWith('-') ? attribute.match(/--?[\w-]+/g) : [attribute];
    for (const token of tokens) {
      check(doc.body.includes(token), `${doc.file}: ${name} is missing ${token}`);
      if (token.startsWith('-')) optionCount++;
    }
  }
}
for (const name of documented.keys()) check(commands.has(name), `Documented command is not registered: ${name}`);
const overview = read('docs/cli.md');
for (const token of ['--help', '-h', '-?', '--version', '-v']) check(overview.includes('`' + token + '`'), `Missing global option ${token}`);

const cliIndex = process.argv.indexOf('--cli');
if (cliIndex >= 0) {
  const dll = path.resolve(process.argv[cliIndex + 1]);
  for (const [name, attributes] of commands) {
    const result = spawnSync('dotnet', [dll, ...name.split(' '), '--help'], { encoding: 'utf8', windowsHide: true });
    check(result.status === 0, `${name}: help failed: ${result.stderr}`);
    const help = result.stdout ?? '';
    for (const attribute of attributes) {
      const tokens = attribute.startsWith('-') ? attribute.match(/--?[\w-]+/g) : [attribute];
      for (const token of tokens) check(help.includes(token), `${name}: source option/argument missing from runtime help: ${token}`);
    }
    for (const token of help.match(/--?[a-z][a-z-]*/g) ?? []) {
      if (['-h', '--help', '-v', '--version'].includes(token)) continue;
      check(documented.get(name)?.body.includes(token), `${name}: runtime option missing from documentation: ${token}`);
    }
  }
  console.log(`Checked runtime --help for all ${commands.size} command paths.`);
}

const pages = [];
for (const file of walk('docs').filter(p => /\.(md|html)$/.test(p) && !/[\\/](_site|vendor|_layouts|_includes)[\\/]/.test(p))) {
  const fm = read(file).match(/^---\r?\n([\s\S]*?)\r?\n---/);
  if (fm) pages.push({ ...Object.fromEntries([...fm[1].matchAll(/^(\w+):\s*(.*?)\s*$/gm)].map(m => [m[1], m[2]])), file });
}
const urls = new Set();
for (const page of pages) {
  if (page.permalink) { check(!urls.has(page.permalink), `Duplicate URL ${page.permalink}`); urls.add(page.permalink); }
  if (page.parent) check(pages.some(p => p.title === page.parent && p.has_children === 'true' && (!page.grand_parent || p.parent === page.grand_parent)), `${page.file}: missing navigation parent ${page.parent}`);
}
for (const url of ['/', '/docs/why-packata/', '/cli/', '/cli-database/', '/database-integration/', '/csv-dialect-export/', '/brand-colors/', '/contact/', '/changelog-timeline/', '/changelog-posts/', '/blog/', '/thanks/', '/404.html']) check(urls.has(url), `Legacy URL lost: ${url}`);

// After Jekyll builds, check actual internal links/fragments and sample downloads.
if (process.argv.includes('--site')) {
  const site = path.resolve('docs/_site'), htmlFiles = walk(site).filter(p => p.endsWith('.html'));
  const origin = 'https://seddryck.github.io';
  const decode = s => s.replace(/&amp;/g, '&').replace(/&#39;/g, "'").replace(/&quot;/g, '"');
  for (const file of htmlFiles) {
    const relative = path.relative(site, file).split(path.sep).join('/').replace(/index\.html$/, '');
    for (const m of read(file).matchAll(/\b(?:href|src)="([^"]+)"/g)) {
      if (/^(mailto:|tel:|data:|javascript:)/.test(m[1])) continue;
      const url = new URL(decode(m[1]), origin + '/Packata/' + relative);
      if (url.origin !== origin) continue;
      check(url.pathname.startsWith('/Packata/'), `${relative}: link escapes base path: ${m[1]}`);
      if (!url.pathname.startsWith('/Packata/')) continue;
      let target = path.join(site, decodeURIComponent(url.pathname.slice('/Packata/'.length)));
      if (fs.existsSync(target) && fs.statSync(target).isDirectory()) target = path.join(target, 'index.html');
      check(fs.existsSync(target), `${relative}: broken local link ${m[1]}`);
      if (url.hash && fs.existsSync(target) && target.endsWith('.html')) {
        const ids = [...read(target).matchAll(/\bid="([^"]+)"/g)].map(x => decode(x[1]));
        check(ids.includes(decodeURIComponent(url.hash.slice(1))), `${relative}: missing fragment ${m[1]}`);
      }
    }
  }
  console.log(`Checked rendered links/fragments in ${htmlFiles.length} HTML pages.`);
}
if (problems.length) { console.error(problems.join('\n')); process.exitCode = 1; }
else console.log(`Documentation covers all ${commands.size} CLI commands, ${optionCount} option/alias occurrences and positional arguments; navigation and legacy URLs pass.`);
