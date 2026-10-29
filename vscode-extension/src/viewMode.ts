export type ViewMode = 'canonical' | 'native';
export interface WorkspacePreference {
  get<T>(key: string): T | undefined;
  update(key: string, value: unknown): Thenable<void>;
}

/** VS Code workspaceState is shared by all roots and survives editor restarts. */
export class ViewPreference {
  constructor(private readonly storage: WorkspacePreference) {}
  get mode(): ViewMode { return this.storage.get<string>('contractViewMode') === 'native' ? 'native' : 'canonical'; }
  async set(mode: ViewMode): Promise<void> { await this.storage.update('contractViewMode', mode); }
}
