import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { execFileSync } from 'node:child_process';
import yauzl from 'yauzl';

const require = createRequire(import.meta.url);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export const targets = {
  'win32-x64': { rid: 'win-x64', executable: 'Packata.LanguageServer.exe', runtime: 'coreclr.dll' },
  'linux-x64': { rid: 'linux-x64', executable: 'Packata.LanguageServer', runtime: 'libcoreclr.so' },
  'darwin-arm64': { rid: 'osx-arm64', executable: 'Packata.LanguageServer', runtime: 'libcoreclr.dylib' }
};
export async function validateVsix(filename, target) {
  const definition = targets[target];
  if (!definition) throw new Error('Unsupported VSIX target.');
  const entries = await new Promise((resolve, reject) => {
    yauzl.open(filename, { lazyEntries: true }, (error, zip) => {
      if (error) return reject(error);
      const names = new Set();
      zip.on('entry', entry => { names.add(entry.fileName); zip.readEntry(); });
      zip.on('end', () => resolve(names)); zip.on('error', reject); zip.readEntry();
    });
  });
  for (const name of ['extension/package.json', 'extension/dist/extension.js',
    `extension/server/${definition.executable}`, `extension/server/${definition.runtime}`,
    'extension/server/Packata.LanguageServer.dll', 'extension/server/Packata.LanguageServer.runtimeconfig.json']) {
    if (!entries.has(name)) throw new Error(`VSIX is missing ${name}`);
  }
  if ([...entries].some(name => name.startsWith('extension/node_modules/') || name.includes('/test/') || name.endsWith('.pdb'))) {
    throw new Error('VSIX contains development-only files.');
  }
}

export async function pack(target, version) {
  const definition = targets[target];
  if (!definition) throw new Error(`Use one of: ${Object.keys(targets).join(', ')}`);
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) throw new Error('Expected a semantic package version.');
  const staging = path.join(root, 'artifacts', `staging-${target}`);
  // This fixed output folder is inside this extension; no user-supplied deletion paths.
  if (!staging.startsWith(root + path.sep)) throw new Error('Unsafe staging location.');
  fs.rmSync(staging, { recursive: true, force: true });
  fs.mkdirSync(staging, { recursive: true });
  const server = path.join(staging, 'server');
  execFileSync('dotnet', ['publish', path.join(root, '../src/Packata.LanguageServer/Packata.LanguageServer.csproj'),
    '-c', 'Release', '-f', 'net10.0', '-r', definition.rid, '--self-contained', 'true',
    '--disable-build-servers', '-p:PublishTrimmed=false', `-p:Version=${version}`, '-o', server],
    { stdio: 'inherit', env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1' } });
  for (const file of fs.readdirSync(server)) if (file.endsWith('.pdb')) fs.unlinkSync(path.join(server, file));
  if (target !== 'win32-x64') fs.chmodSync(path.join(server, definition.executable), 0o755);
  for (const file of ['dist', 'images', 'README.md', 'LICENSE']) fs.cpSync(path.join(root, file), path.join(staging, file), { recursive: true });
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
  manifest.version = version;
  // The compiled bundle includes all runtime JS; only its explicit allowlist goes in the package.
  delete manifest.scripts; delete manifest.dependencies; delete manifest.devDependencies;
  fs.writeFileSync(path.join(staging, 'package.json'), JSON.stringify(manifest, null, 2));
  const output = path.join(root, 'artifacts', `packata-language-support-${version}-${target}.vsix`);
  const vsce = path.join(path.dirname(require.resolve('@vscode/vsce/package.json')), 'vsce');
  execFileSync(process.execPath, [vsce, 'package', '--target', target, '--no-dependencies', '--out', output], { cwd: staging, stdio: 'inherit' });
  await validateVsix(output, target);
  return output;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const index = process.argv.indexOf('--target');
  const target = index >= 0 ? process.argv[index + 1] : `${process.platform}-${process.arch}`;
  const version = process.env.PACKATA_VERSION ?? JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8')).version;
  console.log(await pack(target, version));
}
