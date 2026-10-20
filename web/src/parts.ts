import { signal } from '@preact/signals'
import { ageStep, UNKNOWN_AGE } from './activity'
import { request } from './bridge'
import { LINES_AREA, type Model, type Node } from './model'
import type { FileOutline, LineChange, OutlineSymbol } from './skeleton'

// The map inside a file. Its parts are the outline's declarations and sections, with a level's
// data declarations gathered into a block per kind, and they are measured as files are: the
// commits that changed their lines, when they last changed, and what is uncommitted.

/** A part of a file on the map: a declaration, a section, or a block of data declarations. */
export interface Part {
  /** Dense, for per-part arrays. */
  readonly index: number
  /** What the map shows: a declaration's name, a section's title, a block's kind. */
  readonly name: string
  /** The outline's kind; a block's plural ("constants"); "top" for the lines above the first part; "file". */
  readonly kind: string
  /** The outline's id, for declarations. */
  readonly id: string | null
  readonly from: number
  readonly to: number
  /** Its weight on the map: its lines, a block's members' lines. */
  readonly lines: number
  /** True for a part with no parts inside it, as the layout names leaves. */
  readonly isFile: boolean
  readonly children: readonly Part[]
  readonly parent: Part | null
}

export interface PartTree {
  /** The file itself, holding its parts. */
  readonly root: Part
  /** Every part, by index. */
  readonly parts: readonly Part[]
}

/** Which lines of the working file each loaded commit changed, newest first, and what is uncommitted. */
export interface LineHistory {
  commits: { sha: string; lines: LineChange[] }[]
  working: LineChange[]
}

/** Kinds of data declaration and the block a level's declarations of that kind gather in. */
export const BLOCKS: Readonly<Record<string, string>> = {
  constant: 'constants', variable: 'variables', field: 'fields', property: 'properties', signal: 'signals',
  uniform: 'uniforms', varying: 'varyings', enum: 'enums', type: 'types', event: 'events', delegate: 'delegates',
}
const BLOCK_KINDS = new Set(Object.values(BLOCKS))

/** Lines above the first part that make a part of their own. */
const TOP_MIN = 4

export const isBlock = (part: Part) => BLOCK_KINDS.has(part.kind)

interface Draft {
  index: number
  name: string
  kind: string
  id: string | null
  from: number
  to: number
  lines: number
  isFile: boolean
  children: Draft[]
  parent: Draft | null
}

/** A file's parts from its outline. */
export function partsOf(outline: FileOutline, fileName: string): PartTree {
  const all: Draft[] = []
  const add = (part: Omit<Draft, 'index' | 'isFile' | 'children' | 'parent'>, children: Draft[]): Draft => {
    const draft: Draft = { ...part, index: all.length, isFile: children.length === 0, children, parent: null }
    for (const child of children) child.parent = draft
    all.push(draft)
    return draft
  }
  const symbol = (s: OutlineSymbol): Draft =>
    add({ name: s.name, kind: s.kind, id: s.id ?? null, from: s.from, to: s.to, lines: s.to - s.from + 1 }, level(s.children ?? []))
  // Data declarations of one kind gather in a block when a level has two or more of them.
  function level(symbols: readonly OutlineSymbol[]): Draft[] {
    const parts: Draft[] = []
    const blocks = new Map<string, OutlineSymbol[]>()
    for (const s of symbols) {
      const block = BLOCKS[s.kind]
      const alone = !s.children?.length
      if (block && alone && symbols.filter(o => o.kind === s.kind && !o.children?.length).length >= 2) {
        const members = blocks.get(block) ?? []
        members.push(s)
        blocks.set(block, members)
      } else {
        parts.push(symbol(s))
      }
    }
    for (const [name, members] of blocks) {
      const children = members.map(symbol)
      parts.push(add({
        name, kind: name, id: null,
        from: Math.min(...children.map(c => c.from)),
        to: Math.max(...children.map(c => c.to)),
        lines: children.reduce((sum, c) => sum + c.lines, 0),
      }, children))
    }
    return parts
  }

  const top = level(outline.symbols)
  const first = outline.symbols.length > 0 ? Math.min(...outline.symbols.map(s => s.from)) : outline.lines + 1
  if (first - 1 >= TOP_MIN) top.push(add({ name: 'top of file', kind: 'top', id: null, from: 1, to: first - 1, lines: first - 1 }, []))
  const root = add({ name: fileName, kind: 'file', id: null, from: 1, to: Math.max(1, outline.lines), lines: Math.max(1, outline.lines) }, top)
  return { root, parts: all }
}

/** Does a change touch these lines? Removed lines touch a part when they were between two of its lines. */
function touches(change: LineChange, from: number, to: number): boolean {
  return change.to >= change.from ? change.from <= to && change.to >= from : change.from > from && change.from <= to
}

/** What each part's history holds: the loaded commits that changed it, and its uncommitted change. */
export interface PartHistory {
  /** Per part, indices into model.commits, oldest first. */
  readonly commits: readonly Int32Array[]
  /** Per part: 0 unchanged, 1 changed, 2 every line new since the last commit. */
  readonly change: Uint8Array
}

/**
 * Each part's commits and uncommitted change. A block counts what changed any of its members; a
 * file new at its path is new in every part.
 */
export function partHistory(tree: PartTree, history: LineHistory | null, model: Model, fresh: boolean): PartHistory {
  const index = new Map(model.commits.map((c, i) => [c.sha, i]))
  const loaded = (history?.commits ?? []).map(c => ({ at: index.get(c.sha), lines: c.lines })).filter(c => c.at !== undefined)
  const commits: Int32Array[] = new Array(tree.parts.length)
  const change = new Uint8Array(tree.parts.length)
  const working = history?.working ?? []

  const measure = (part: Part): void => {
    for (const child of part.children) measure(child)
    if (isBlock(part)) {
      commits[part.index] = Int32Array.from(new Set(part.children.flatMap(c => [...commits[c.index]]))).sort()
      const marks = part.children.map(c => change[c.index])
      change[part.index] = fresh || (marks.length > 0 && marks.every(m => m === 2)) ? 2 : marks.some(m => m > 0) ? 1 : 0
      return
    }
    commits[part.index] = Int32Array.from(loaded.filter(c => c.lines.some(l => touches(l, part.from, part.to))).map(c => c.at!)).sort()
    if (fresh) {
      change[part.index] = 2
      return
    }
    const hits = working.filter(l => touches(l, part.from, part.to))
    if (hits.length === 0) return
    let added = 0
    for (const l of hits) if (l.to >= l.from) added += Math.min(l.to, part.to) - Math.max(l.from, part.from) + 1
    change[part.index] = added >= part.to - part.from + 1 ? 2 : 1
  }
  measure(tree.root)
  return { commits, change }
}

/** Commits in the range [from, to) that changed each part. */
export function partActivity(history: PartHistory, from: number, to: number): Int32Array {
  return Int32Array.from(history.commits, list => list.reduce((n, at) => n + (at >= from && at < to ? 1 : 0), 0))
}

/** When each part last changed up to the range's end, and anywhere in the loaded history; 0 for never. */
export function partTimes(history: PartHistory, model: Model, to: number): { times: Float64Array; ever: Float64Array } {
  const time = (at: number) => model.commits[at]?.time ?? 0
  const times = new Float64Array(history.commits.length)
  const ever = new Float64Array(history.commits.length)
  history.commits.forEach((list, i) => {
    for (const at of list) {
      ever[i] = Math.max(ever[i], time(at))
      if (at < to) times[i] = Math.max(times[i], time(at))
    }
  })
  return { times, ever }
}

/** A part's age step, by the rule files follow: unknown unless the history says, older only past a short history. */
export function partAgeStep(time: number, ever: number, asOf: number, olderThanLoaded: boolean, change: number): number {
  if (time > 0) return ageStep(time, asOf)
  return olderThanLoaded && ever === 0 && change !== 2 ? 0 : UNKNOWN_AGE
}

/** The largest parts a file's tooltip names: declarations and blocks, at most three, when it has two or more. */
export function largestParts(tree: PartTree, count = 3): Part[] {
  const candidates = tree.parts.filter(p => p.kind !== 'top' && p.kind !== 'file' && (isBlock(p) || (p.isFile && !(p.parent && isBlock(p.parent)))))
  if (candidates.length < 2) return []
  return [...candidates].sort((a, b) => b.lines - a.lines || a.from - b.from).slice(0, count)
}

/** The part a choice names: by its outline id, else by its lines. */
export function findPart(tree: PartTree, choice: { id: string | null; from: number; to: number } | null): Part | null {
  if (!choice) return null
  return tree.parts.find(p => (choice.id ? p.id === choice.id : p.id === null && p.from === choice.from && p.to === choice.to)) ?? null
}

// Outlines and histories, asked of the host once per version of a file.

/** Bumped when an outline or a history arrives, so the map and its tooltips redraw. */
export const partsLoaded = signal(0)

interface Entry<T> {
  key: string
  /** Undefined while it loads. */
  value: T | null | undefined
  promise: Promise<T | null>
}

const LIMIT = 300
const outlines = new Map<string, Entry<FileOutline>>()
const histories = new Map<string, Entry<LineHistory>>()
const trees = new WeakMap<FileOutline, PartTree>()

const fileKey = (node: Node) => `${node.src.modified ?? ''}:${node.src.bytes ?? ''}:${node.src.change ?? ''}`

function cached<T>(table: Map<string, Entry<T>>, path: string, key: string, load: () => Promise<T | null>): Entry<T> {
  const entry = table.get(path)
  if (entry && entry.key === key) return entry
  const fresh: Entry<T> = { key, value: undefined, promise: Promise.resolve(null) }
  fresh.promise = load().then(value => value, () => null).then(value => {
    fresh.value = value
    partsLoaded.value++
    return value
  })
  table.delete(path)
  table.set(path, fresh)
  if (table.size > LIMIT) table.delete(table.keys().next().value!)
  return fresh
}

const askOutline = (node: Node) => () =>
  request<{ outline?: FileOutline | null }>({ type: 'outline', path: node.path }).then(r => r.outline ?? null)

/** Only code, tests and documents have outlines. */
const outlined = (node: Node) => node.isFile && node.kind !== null && LINES_AREA.has(node.kind)

/** A file's outline: undefined while it loads, null when it has none. */
export function outlineOf(node: Node): FileOutline | null | undefined {
  return outlined(node) ? cached(outlines, node.path, fileKey(node), askOutline(node)).value : null
}

/** A file's outline when it arrives. */
export function loadOutline(node: Node): Promise<FileOutline | null> {
  return outlined(node) ? cached(outlines, node.path, fileKey(node), askOutline(node)).promise : Promise.resolve(null)
}

/** A file's parts, built once per outline. */
export function treeOf(node: Node, outline: FileOutline): PartTree {
  let tree = trees.get(outline)
  if (!tree) {
    tree = partsOf(outline, node.name)
    trees.set(outline, tree)
  }
  return tree
}

/** Which lines each commit changed: undefined while it loads, null without history. */
export function lineHistoryOf(model: Model, node: Node): LineHistory | null | undefined {
  if (!model.scan.project.git) return null
  return cached(histories, node.path, `${model.scan.project.head ?? ''}:${fileKey(node)}`, () =>
    request<{ history?: LineHistory | null }>({ type: 'lineHistory', path: node.path }).then(r => r.history ?? null)).value
}
