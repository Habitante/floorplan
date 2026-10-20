import { batch, computed, effect, signal } from '@preact/signals'
import { ages, countActivity, markCommit, markNodes } from './activity'
import { buildModel, weightOf, type Footprint, type Model, type Node } from './model'
import { systemMarks, systemsOf } from './meaning'
import { onSince, sinceRange, sinceStart } from './since'
import type { DescriptionReport, PointerItem, Scan, SinceReport } from './types'

export type Lens = 'activity' | 'age' | 'kind' | 'systems'

export interface Status {
  state: 'idle' | 'scanning' | 'ready' | 'error'
  path?: string
  phase?: string
  done?: number
  total?: number
  message?: string
  /** An automatic refresh is running in the background. */
  refreshing?: boolean
}

export const DEFAULT_RANGE = 40

export const model = signal<Model | null>(null)
export const status = signal<Status>({ state: 'idle' })
/** The folder the map is zoomed into. */
export const focus = signal<Node | null>(null)
export const selected = signal<Node | null>(null)
export const hovered = signal<Node | null>(null)
/** Index into model.commits (oldest first), or the uncommitted working changes. */
export const selectedCommit = signal<number | 'working' | null>(null)
/** The file open in the reader, optionally compared with a commit's parent or with HEAD. */
export interface ReaderTarget {
  node: Node
  commit: number | 'working' | null
}
export const reader = signal<ReaderTarget | null>(null)

/** Lines of the reader in view and selected, as the reader reports them. */
export interface ReaderLines {
  visible: [number, number] | null
  selection: [number, number] | null
  /** The selected text, which also covers a diff's removed lines. */
  text: string | null
  /** The selection includes lines a diff shows as removed. */
  removed: boolean
}
export const NO_LINES: ReaderLines = { visible: null, selection: null, text: null, removed: false }
export const readerLines = signal<ReaderLines>(NO_LINES)
/** Lines the reader should bring into view for a pointer, once it shows that file. */
export const readerFocus = signal<{ path: string; from: number; to: number } | null>(null)

/** The file finder (Ctrl+P) is open. */
export const finding = signal(false)

/** The theme the window is drawn in. */
export const theme = signal<'light' | 'dark'>('light')

/** An agent's pointer: paths outlined on the map with a caption, until replaced or dismissed. */
export interface Pointer {
  agent: string
  caption: string
  items: PointerItem[]
  /** When the agent pointed, Unix milliseconds. */
  at: number
  /** When it arrived, on the performance clock, for the arrival pulse. */
  arrived: number
}
/** Oldest first, at most one per agent. */
export const pointers = signal<Pointer[]>([])

/** Commits counted by the activity lens: [from, to) into model.commits. */
export const range = signal<[number, number]>([0, 0])
/** What landed while the human was away, as the host measured it; null once dismissed. */
export const since = signal<SinceReport | null>(null)
export const footprint = signal<Footprint>(stored('footprint', ['lines', 'bytes'], 'lines'))
export const lens = signal<Lens>(stored('lens', ['activity', 'age', 'kind', 'systems'], 'activity'))

/** The project's description as the host checked it against the scan; null when it has none. */
export const description = signal<DescriptionReport | null>(null)
/** The system picked in the inspector, by index into the description's systems: its files stay lit. */
export const selectedSystem = signal<number | null>(null)

/** Each node's system for the systems lens: a file's own, a folder's by weight. */
export const systemMap = computed(() => (model.value ? systemsOf(model.value, description.value, footprint.value) : null))
/** The picked system's files and folders, to dim the rest of the map. */
export const isolated = computed(() => (model.value ? systemMarks(model.value, description.value, selectedSystem.value) : null))

/** Whether the description names any systems, so the systems lens has something to show. */
export const hasSystems = computed(() => (description.value?.description?.systems.length ?? 0) > 0)

export const activity = computed(() => {
  const m = model.value
  if (!m) return null
  const [from, to] = range.value
  return countActivity(m, from, to)
})

/**
 * Each node's last commit time within commits[0, to), measured from the range's newest commit: a
 * project keeps its own pace while work pauses, and an old checkout is not all one step.
 */
export const age = computed(() => {
  const m = model.value
  if (!m) return null
  const to = range.value[1]
  return ages(m, to, m.commits[to - 1]?.time ?? 0)
})

export const touched = computed(() => {
  const m = model.value
  const c = selectedCommit.value
  if (!m || c === null) return null
  return c === 'working' ? markNodes(m, m.changed) : markCommit(m, c)
})

effect(() => store('footprint', footprint.value))
effect(() => store('lens', lens.value))

/** A range by its commits, since indices shift as history grows: "latest N", or a pair of commits. */
interface StoredRange {
  latest?: number
  from?: string
  to?: string
}

/** Per-project view: the zoomed folder and the range. */
interface StoredView extends StoredRange {
  focus?: string
}

function storeRange(m: Model, [from, to]: readonly [number, number]): StoredRange {
  return to === m.commits.length ? { latest: to - from } : { from: m.commits[from]?.sha, to: m.commits[to - 1]?.sha }
}

/** The stored range in this history, or the default when it no longer resolves. */
function resolveRange(m: Model, stored: StoredRange | null): [number, number] {
  const n = m.commits.length
  if (stored?.latest !== undefined) return [Math.max(0, n - stored.latest), n]
  if (stored?.from && stored.to) {
    const start = m.commits.findIndex(c => c.sha === stored.from)
    const end = m.commits.findIndex(c => c.sha === stored.to)
    if (start >= 0 && end >= start) return [start, end + 1]
  }
  return [Math.max(0, n - DEFAULT_RANGE), n]
}

const viewKey = (m: Model) => `view.${m.scan.project.root.toLowerCase()}`

effect(() => {
  const m = model.value
  const f = focus.value
  if (!m || !f) return
  const view: StoredView = { focus: f.path, ...storeRange(m, range.value) }
  store(viewKey(m), JSON.stringify(view))
})

function restoredView(m: Model): { focus: Node; range: [number, number] } | null {
  let view: StoredView
  try {
    view = JSON.parse(localStorage.getItem(`map.${viewKey(m)}`) ?? 'null') as StoredView
  } catch {
    return null
  }
  if (!view) return null
  const node = view.focus !== undefined ? m.byPath.get(view.focus) : undefined
  return { focus: node && !node.isFile ? node : m.root, range: resolveRange(m, view) }
}

/** The range in place before the briefing's was applied, to switch back to. */
let beforeSince: StoredRange | null = null

/** Applies a scan. A briefing comes with the scan that opens a project, and starts the range at its commits. */
export function applyScan(scan: Scan, briefing?: SinceReport, report: DescriptionReport | null = null) {
  const previous = model.value
  const next = buildModel(scan)
  const same = previous?.scan.project.root === scan.project.root
  const carry = (n: Node | null) => (same && n ? next.byPath.get(n.path) ?? null : null)
  const n = next.commits.length

  let nextRange: [number, number] = [Math.max(0, n - DEFAULT_RANGE), n]
  if (same && previous) {
    const [from, to] = range.value
    const followsLatest = to === previous.commits.length
    // A range since the human was last here keeps its start, so commits that land join it.
    const anchor = followsLatest && from === sinceStart(previous, since.value) ? sinceStart(next, since.value) : -1
    if (anchor >= 0) {
      nextRange = [anchor, n]
    } else if (followsLatest) {
      nextRange = [Math.max(0, n - (to - from)), n]
    } else {
      const start = next.commits.findIndex(c => c.sha === previous.commits[from]?.sha)
      const end = next.commits.findIndex(c => c.sha === previous.commits[to - 1]?.sha)
      if (start >= 0 && end >= start) nextRange = [start, end + 1]
    }
  }

  let nextCommit: number | 'working' | null = null
  const commit = selectedCommit.value
  if (same && previous && commit !== null) {
    if (commit === 'working') nextCommit = next.changed.length > 0 ? 'working' : null
    else {
      const index = next.commits.findIndex(c => c.sha === previous.commits[commit]?.sha)
      nextCommit = index >= 0 ? index : null
    }
  }

  const restored = same ? null : restoredView(next)
  if (restored) nextRange = restored.range
  if (!same) {
    const opened = sinceRange(next, briefing ?? null)
    beforeSince = opened ? storeRange(next, nextRange) : null
    if (opened) nextRange = opened
  }

  batch(() => {
    model.value = next
    focus.value = carry(focus.value) ?? restored?.focus ?? next.root
    selected.value = carry(selected.value)
    hovered.value = null
    selectedCommit.value = nextCommit
    range.value = nextRange
    const open = reader.value
    const openNode = open && same ? next.byPath.get(open.node.path) : undefined
    const openCommit = open && typeof open.commit === 'number'
      ? next.commits.findIndex(c => c.sha === previous?.commits[open.commit as number]?.sha)
      : open?.commit ?? null
    reader.value = openNode && openCommit !== -1 ? { node: openNode, commit: openCommit } : null
    if (!same) {
      pointers.value = []
      since.value = briefing ?? null
      selectedSystem.value = null
    }
    // Re-checked with every scan, so anchors that broke show as stale.
    receiveDescription(report)
    status.value = { state: 'ready', path: scan.project.root }
  })
}

/**
 * A briefing for a human back at an open window. The window is a live view and nothing in it
 * switches by itself: the chip offers the commits since, and the range moves only when clicked.
 */
export function receiveSince(briefing: SinceReport) {
  since.value = briefing
}

/** A description checked against the scan on screen: with each scan, and when an agent saves a new one. */
export function receiveDescription(report: DescriptionReport | null) {
  batch(() => {
    description.value = report
    const systems = report?.description?.systems.length ?? 0
    if (selectedSystem.value !== null && selectedSystem.value >= systems) selectedSystem.value = null
    if (lens.value === 'systems' && systems === 0) lens.value = 'kind'
  })
}

/** Switches between the commits since the human was last here and the range they had before. */
export function toggleSince() {
  const m = model.peek()
  const briefing = since.peek()
  const target = m && sinceRange(m, briefing)
  if (!m || !target) return
  if (onSince(m, briefing, range.peek())) {
    range.value = resolveRange(m, beforeSince)
    beforeSince = null
  } else {
    beforeSince = storeRange(m, range.peek())
    range.value = target
  }
}

/** Puts the briefing away; the range stays as it is. */
export function dismissSince() {
  since.value = null
  beforeSince = null
}

/** Zoom to a folder (a file zooms to its folder). */
export function zoomTo(node: Node | null) {
  if (!node) return
  focus.value = node.isFile ? node.parent ?? node : node
}

/** Opens a file in the reader and selects it on the map. */
export function openReader(node: Node, commit: number | 'working' | null = null) {
  if (!node.isFile) return
  batch(() => {
    showOnMap(node)
    selected.value = node
    reader.value = { node, commit }
  })
}

/** Zooms out to the node's folder if the map is focused elsewhere. */
function showOnMap(node: Node) {
  for (let n: Node | null = node; n; n = n.parent) if (n === focus.value) return
  zoomTo(node.parent ?? node)
}

/**
 * Shows a file or folder chosen by name. A folder becomes the view. A file is read in its folder,
 * as clicking it does: within a selected commit or the uncommitted changes that touched it, its
 * diff. Something without area under the footprint is marked on the nearest folder that has area,
 * as uncommitted changes and pointers are.
 */
export function jumpTo(node: Node) {
  const weight = weightOf(footprint.peek())
  let view: Node = node
  if (weight(node) === 0) {
    // The view in which the nearest folder with area is drawn, and so carries the selection.
    let marked = node.parent
    while (marked?.parent && weight(marked) === 0) marked = marked.parent
    view = marked?.parent ?? marked ?? node
  } else if (node.isFile) {
    view = node.parent ?? node
  }
  batch(() => {
    focus.value = view
    if (node.isFile) {
      const commit = selectedCommit.peek()
      openReader(node, commit !== null && touched.peek()?.[node.index] ? commit : null)
    } else {
      selectedCommit.value = null
      reader.value = null
      selected.value = view === node ? null : node
    }
  })
}

/** Select a node and make sure the map shows it. */
export function reveal(node: Node) {
  const f = focus.value
  let inside = false
  for (let n: Node | null = node; n; n = n.parent) if (n === f) inside = true
  batch(() => {
    if (!inside) zoomTo(node.parent ?? node)
    selected.value = node
    selectedCommit.value = null
  })
}

function stored<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  try {
    const value = localStorage.getItem(`map.${key}`) as T | null
    return value && allowed.includes(value) ? value : fallback
  } catch {
    return fallback
  }
}

function store(key: string, value: string) {
  try {
    localStorage.setItem(`map.${key}`, value)
  } catch {
    // Preferences are a convenience; ignore unavailable storage.
  }
}
