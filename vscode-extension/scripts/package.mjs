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

export function resolveDotnet(environment = process.env, platform = process.platform) {
  let defaultRoot = '/usr/share/dotnet';
  if (platform === 'win32') defaultRoot = path.join(environment.ProgramFiles ?? String.raw`C:\Program Files`, 'dotnet');
  if (platform === 'darwin') defaultRoot = '/usr/local/share/dotnet';
  const sdkRoot = environment.DOTNET_ROOT || defaultRoot;
  if (!path.isAbsolute(sdkRoot)) throw new Error('DOTNET_ROOT must be an absolute SDK installation directory.');
  const executable = path.join(sdkRoot, platform === 'win32' ? 'dotnet.exe' : 'dotnet');
  if (!fs.existsSync(executable) || !fs.statSync(executable).isFile()) {
    throw new Error('The .NET SDK executable was not found. Set DOTNET_ROOT to its installation directory.');
  }
  return fs.realpathSync(executable);
}

export function validateExecutableMode(mode) {
  if ((mode & 0o111) === 0 || (mode & 0o022) !== 0) {
    throw new Error('The published server must be executable and must not be writable by group or others. Republish it with the .NET SDK.');
  }
}

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
    'extension/server/Packata.LanguageServer.dll', 'extension/server/Packata.LanguageServer.runtimeconfig.json',
    'extension/server/Packata.Application.dll', 'extension/server/Didot.Core.dll',
    'extension/server/Scriban.dll', 'extension/server/Handlebars.dll', 'extension/server/DotLiquid.dll',
    'extension/server/Morestachio.dll', 'extension/server/SmartFormat.dll', 'extension/server/Antlr4.StringTemplate.dll']) {
    if (!entries.has(name)) throw new Error(`VSIX is missing ${name}`);
  }
  if ([...entries].some(name => name.startsWith('extension/node_modules/') || name.includes('/test/') || name.endsWith('.pdb'))) {
    throw new Error('VSIX contains development-only files.');
  }
  if (entries.has('extension/server/Polars.CSharp.dll')) throw new Error('Schema text generation must not package the Polars execution runtime.');
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
  execFileSync(resolveDotnet(), ['publish', path.join(root, '../src/Packata.LanguageServer/Packata.LanguageServer.csproj'),
    '-c', 'Release', '-f', 'net10.0', '-r', definition.rid, '--self-contained', 'true',
    '--disable-build-servers', '-p:PublishTrimmed=false', `-p:Version=${version}`, '-o', server],
    { stdio: 'inherit', env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: '1' } });
  for (const file of fs.readdirSync(server)) if (file.endsWith('.pdb')) fs.unlinkSync(path.join(server, file));
  // dotnet publish sets the native apphost's executable mode. Validate it on Unix rather
  // than broadening permissions; a Windows cross-publish cannot establish Unix file modes.
  if (process.platform !== 'win32' && target !== 'win32-x64') {
    validateExecutableMode(fs.statSync(path.join(server, definition.executable)).mode);
  }
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
