import * as fs from 'node:fs';
import * as path from 'node:path';

export function resolveServer(root: string, override: string, platform = process.platform): string {
  if (override && !path.isAbsolute(override)) throw new Error('Packata server override must be an absolute executable path.');
  const executable = override || path.join(root, 'server', platform === 'win32' ? 'Packata.LanguageServer.exe' : 'Packata.LanguageServer');
  if (!fs.existsSync(executable) || !fs.statSync(executable).isFile()) {
    throw new Error('Packata language server is missing. Install the VSIX for your platform or set packata.languageServer.path.');
  }
  return executable;
}
