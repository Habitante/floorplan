import * as format from './format'
import type { Model, Node } from './model'
import type { WorkingChange } from './types'

// Uncommitted changes as the map shows them. Violet is the one colour for "uncommitted", and
// shape says which kind: an outline for a modified file, a filled corner as well for a new one,
// and counts in the region headers, where deleted files (which take no area) show too.
// The interface has three words for them, New, Modified and Deleted (+ ~ − in headers, N M D in
// lists); Git's own terms (untracked, added, renamed) stay in the scan and in agents' replies.

export type ChangeKind = 'new' | 'modified' | 'deleted'

/** A rename is new at its new path, as Activity counts a rename as a delete and an add. */
export function changeKind(change: WorkingChange): ChangeKind {
  return change === 'deleted' ? 'deleted' : change === 'modified' ? 'modified' : 'new'
}

const KIND_WORD: Record<ChangeKind, string> = { new: 'New', modified: 'Modified', deleted: 'Deleted' }
const KIND_MARK: Record<ChangeKind, string> = { new: 'N', modified: 'M', deleted: 'D' }

/** "New, uncommitted": the kind of change, in the interface's words. */
export const changeLabel = (change: WorkingChange) => `${KIND_WORD[changeKind(change)]}, uncommitted`

/** N, M or D, for lists. */
export const changeMark = (change: WorkingChange) => KIND_MARK[changeKind(change)]

/** What a file's change says in words, with a renamed file's old path. */
export function describeChange(node: Node): string | null {
  const change = node.src.change
  if (!change) return null
  return node.src.renamedFrom ? `${changeLabel(change)}, renamed from ${node.src.renamedFrom}` : changeLabel(change)
}

export interface ChangeCounts {
  new: number
  modified: number
  deleted: number
}

const KINDS: readonly ChangeKind[] = ['new', 'modified', 'deleted']
const counted = new WeakMap<Model, Int32Array>()

/** Uncommitted files at or below the node, by kind of change. */
export function changeCounts(m: Model, node: Node): ChangeCounts {
  let table = counted.get(m)
  if (!table) {
    table = new Int32Array(m.nodes.length * KINDS.length)
    for (const file of m.changed) {
      const k = KINDS.indexOf(changeKind(file.src.change!))
      for (let n: Node | null = file; n; n = n.parent) table[n.index * KINDS.length + k]++
    }
    counted.set(m, table)
  }
  const i = node.index * KINDS.length
  return { new: table[i], modified: table[i + 1], deleted: table[i + 2] }
}

/** "+2 ~1 −1": the counts that are not zero, as the region headers show them. */
export function changeSummary(c: ChangeCounts): string {
  const parts: string[] = []
  if (c.new) parts.push(`+${format.int(c.new)}`)
  if (c.modified) parts.push(`~${format.int(c.modified)}`)
  if (c.deleted) parts.push(`−${format.int(c.deleted)}`)
  return parts.join(' ')
}

/** The same in words, for tooltips and the inspector. */
export function changeWords(c: ChangeCounts): string {
  const parts: string[] = []
  if (c.new) parts.push(`${format.int(c.new)} new`)
  if (c.modified) parts.push(`${format.int(c.modified)} modified`)
  if (c.deleted) parts.push(`${format.int(c.deleted)} deleted`)
  return parts.join(', ')
}

/** New files a refresh brought onto the map: new, and not on the map before. */
export function arrivedPaths(previous: Model, next: Model): string[] {
  return next.changed.filter(n => changeKind(n.src.change!) === 'new' && !previous.byPath.has(n.path)).map(n => n.path)
}
