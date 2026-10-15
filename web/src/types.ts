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

/** What the window shows, reported to the host so agents can resolve "this" (FloorPlan.Core's BoardView). */
export interface BoardView {
  /** The folder the map is zoomed into; '' for the whole project. */
  focus: string
  selected: string | null
  /** The commit selected on the history strip, 'working' for the uncommitted changes. */
  commit: string | null
  /** Oldest and newest commits counted by the activity lens. */
  range: { from: string | null; to: string | null; count: number; latest: boolean } | null
  reader: {
    path: string
    /** A SHA when the reader shows that commit's diff, 'working' for the uncommitted diff. */
    commit: string | null
    /** First and last line in view. */
    visible: [number, number] | null
    /** First and last line of the selected text. */
    selection: [number, number] | null
  } | null
  area: 'lines' | 'bytes'
  fill: 'activity' | 'kind'
}
