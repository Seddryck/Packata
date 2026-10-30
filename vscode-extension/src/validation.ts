import { WorkspacePreference } from './viewMode';
import { Position } from './protocol';

export interface ValidatorOption {
  id: string; label: string; description: string; formats: string[]; blockedBySelected: string[];
}
export interface ValidationOptions { schemaVersion: number; format: string; validators: ValidatorOption[] }
export type CheckState = 'Passed' | 'Failed' | 'NotSelected' | 'Blocked' | 'Cancelled' | 'ExecutionError';
export interface ValidationCheck { id: string; label: string; state: CheckState; errors: number; reason?: string }
export interface ValidationReport {
  schemaVersion: number; uri: string; version: number | null; valid: boolean; format: string;
  checks: ValidationCheck[];
  diagnostics: { code: string; severity: string; sourcePath: string; message: string; layer: string;
    range?: { start: Position; end: Position } }[];
}

export class ValidationPreference {
  constructor(private readonly storage: WorkspacePreference) {}
  get(format: string): string[] | undefined {
    const value = this.storage.get<unknown>('validationChecks.v1.' + format);
    return Array.isArray(value) && value.every(id => typeof id === 'string') ? value : undefined;
  }
  async set(format: string, ids: readonly string[]): Promise<void> {
    if (!ids.length) throw new Error('Select at least one check.');
    await this.storage.update('validationChecks.v1.' + format, [...ids]);
  }
}

export function available(options: ValidationOptions): ValidatorOption[] {
  return options.validators.filter(option => option.formats.includes(options.format));
}
export function selectionAvailable(ids: readonly string[] | undefined, options: ValidationOptions): ids is string[] {
  return !!ids?.length && new Set(ids).size === ids.length && ids.every(id => available(options).some(option => option.id === id));
}
export function initialSelection(options: ValidationOptions, saved?: string[]): string[] {
  // New registry entries never enter a default or saved selection implicitly.
  return (saved ?? ['schema', 'semantic']).filter(id => available(options).some(option => option.id === id));
}
export function scopeLabel(options: ValidationOptions, ids: readonly string[]): string {
  return ids.map(id => options.validators.find(option => option.id === id)?.label ?? id).join(' + ');
}
export function checkLabel(check: ValidationCheck): string {
  if (check.state === 'NotSelected') return 'not run';
  if (check.state === 'Failed') {
    if (!check.errors) return 'failed';
    const noun = check.errors === 1 ? 'error' : 'errors';
    return `${check.errors} ${noun}`;
  }
  return check.state === 'ExecutionError' ? 'execution error' : check.state.toLowerCase();
}
export function validationSummary(report: ValidationReport): string {
  return report.checks.map(check => `${check.label} ${check.state === 'Passed' ? 'checks passed' : checkLabel(check)}`).join(' · ');
}
