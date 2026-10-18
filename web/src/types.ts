// The scan contract produced by FloorPlan.Core (camelCase JSON). Absent numbers are unavailable, not zero.

export type FileKind = 'code' | 'test' | 'docs' | 'data' | 'asset' | 'other'
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
  /** A renamed file's path before the rename. */
  renamedFrom?: string
  note?: string
  // Both
  bytes?: number
  modified?: string
  // Folders
  files?: number
  code?: number
  tests?: number
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
  /** Names in Co-authored-by lines, where agents committing under their human's name sign. */
  coAuthors?: string[]
}

export interface Tally {
  name: string
  commits: number
}

/** What landed while the human was away, as the host measured it (FloorPlan.Core's SinceReport). */
export interface SinceReport {
  /** The HEAD the human last saw, abbreviated. */
  seenHead: string
  /** When the human was last there. */
  seenAt: string
  /** Commits since, as git log seen..HEAD counts them. */
  commits: number
  /** The oldest of them in the loaded history, abbreviated: where the range starts. */
  from?: string
  /** Some are older than the loaded history. */
  beyondHistory?: boolean
  /** Why nothing is counted. */
  problem?: string
  /** Top-level folders and files of the map they touched, those with most commits first. */
  regions?: Tally[]
  authors?: Tally[]
  coAuthors?: Tally[]
  /** How many more names each list leaves out. */
  moreRegions?: number
  moreAuthors?: number
  moreCoAuthors?: number
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
    /** The selected text itself. */
    text: string | null
    /** The selection includes lines a diff shows as removed, which have no number. */
    removed: boolean
  } | null
  area: 'lines' | 'bytes'
  fill: 'activity' | 'age' | 'kind'
  theme: 'light' | 'dark'
}

/** A file that changes in the same commits as another (FloorPlan.Core's Partner). */
export interface Partner {
  path: string
  /** Loaded commits that touched both. */
  together: number
  /** Loaded commits that touched the partner. */
  commits: number
  /** 0 to 1, with commits touching many files counting for less. */
  strength: number
  /** Shared commits, newest first, abbreviated; at most a few. */
  shared: string[]
}

/** What changes with a file over the loaded history, and what its name pairs it with (FloorPlan.Core's FileRelations). */
export interface FileRelations {
  path: string
  /** Loaded commits that touched the file. */
  commits: number
  changesWith: Partner[]
  /** For code, its tests by naming convention; for a test, the code it is named for. */
  pairedByName: string[]
}

/** One thing an agent points at: a path of the scan, optionally a file's lines. */
export interface PointerItem {
  path: string
  from?: number
  to?: number
  /** What the agent named when it is not on the map and this is the nearest folder that is. */
  note?: string
}
