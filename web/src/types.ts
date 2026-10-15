// The scan contract produced by FloorPlan.Core (camelCase JSON). Absent numbers are unavailable, not zero.

export type FileKind = 'code' | 'docs' | 'data' | 'asset' | 'other'
export type EntryStatus = 'ok' | 'missing' | 'unreadable' | 'link' | 'repository'
export type WorkingChange = 'modified' | 'added' | 'deleted' | 'renamed' | 'untracked'

export interface ScanNode {
  name: string
  path: string
  // Files
  id?: number
  kind?: FileKind
  status?: EntryStatus
  lines?: number
  change?: WorkingChange
  note?: string
  // Both
  bytes?: number
  modified?: string
  // Folders
  files?: number
  code?: number
  docs?: number
  data?: number
  unmeasured?: number
  children?: ScanNode[]
}

export interface Commit {
  sha: string
  /** Committer time, Unix seconds. */
  time: number
  author: string
  subject: string
  /** Ids of current files touched. */
  files: number[]
  /** Touched paths no longer in the inventory. */
  other: number
}

export interface ProjectInfo {
  root: string
  name: string
  scope: 'tracked' | 'tracked+untracked' | 'folder'
  git: boolean
  head?: string
  branch?: string
  commitLimit: number
  historyComplete: boolean
  /** Files with uncommitted changes, including hidden untracked files. */
  changes: number
  /** Untracked files outside the current scope. */
  untrackedHidden: number
  scannedAt: string
  durationMs: number
}

export interface Scan {
  version: number
  project: ProjectInfo
  tree: ScanNode
  /** Newest first, as Git lists them. */
  commits: Commit[]
  issues: { path?: string; message: string }[]
}
