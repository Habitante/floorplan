import { contains, type Footprint, type Model, type Node } from './model'
import type { Lens, PartChoice, Pointer, ReaderLines, ReaderTarget } from './state'
import type { BoardView, PointerItem } from './types'

// The shared board, front-end side: what the human is looking at goes to the host for agents,
// and agents' pointers come back to be drawn on the map. Pure functions; board-controller.ts
// wires them to the window.

const POINTER_LIMIT = 4
const PULSE_MS = 1400

/** Adds an agent's pointer, replacing its previous one; a pointer without paths clears it. */
export function applyPointer(list: readonly Pointer[], pointer: Pointer, limit = POINTER_LIMIT): Pointer[] {
  const others = list.filter(p => p.agent !== pointer.agent)
  return pointer.items.length === 0 ? others : [...others, pointer].slice(-limit)
}

/** The nodes the pointers name; paths that left the scan since are skipped. */
export function pointedNodes(m: Model, list: readonly Pointer[]): Node[] {
  const nodes: Node[] = []
  for (const pointer of list) {
    for (const item of pointer.items) {
      const node = m.byPath.get(item.path)
      if (node && !nodes.includes(node)) nodes.push(node)
    }
  }
  return nodes
}

/** Line ranges the pointers mark in one file. */
export function pointedLines(list: readonly Pointer[], path: string): [number, number][] {
  const ranges: [number, number][] = []
  for (const pointer of list) {
    for (const item of pointer.items) {
      if (item.path === path && item.from !== undefined) ranges.push([item.from, item.to ?? item.from])
    }
  }
  return ranges
}

/** An item as agents write it: "path", "path:from" or "path:from-to". */
export function describeItem(item: PointerItem): string {
  if (item.note) return item.note
  if (item.from === undefined) return item.path
  return item.to !== undefined && item.to !== item.from ? `${item.path}:${item.from}-${item.to}` : `${item.path}:${item.from}`
}

/** The deepest folder holding all the nodes. */
export function commonFolder(nodes: readonly Node[]): Node | null {
  let folder: Node | null = null
  for (const node of nodes) {
    const own = node.isFile ? node.parent : node
    if (!own) continue
    if (!folder) {
      folder = own
      continue
    }
    while (folder && !contains(folder, own)) folder = folder.parent
  }
  return folder
}

/** How strongly to emphasise newly arrived pointers, 1 on arrival fading to 0. */
export function pulse(list: readonly Pointer[], now: number): number {
  let strongest = 0
  for (const p of list) strongest = Math.max(strongest, 1 - (now - p.arrived) / PULSE_MS)
  return Math.max(0, strongest) ** 2
}

/** The window's view as the host's BoardView contract describes it. */
export function boardView(
  m: Model,
  view: {
    focus: Node | null
    selected: Node | null
    /** The part of the selected file picked on the map inside it. */
    part?: PartChoice | null
    commit: number | 'working' | null
    range: [number, number]
    reader: ReaderTarget | null
    lines: ReaderLines
    area: Footprint
    fill: Lens
    theme: 'light' | 'dark'
  },
): BoardView {
  const sha = (index: number) => m.commits[index]?.sha ?? null
  const [from, to] = view.range
  const open = view.reader
  return {
    focus: view.focus?.path ?? '',
    selected: view.selected?.path ?? null,
    part: view.part && view.part.path === view.selected?.path
      ? { id: view.part.id, name: view.part.name, kind: view.part.kind, from: view.part.from, to: view.part.to }
      : null,
    commit: view.commit === null ? null : view.commit === 'working' ? 'working' : sha(view.commit),
    range: m.commits.length > 0 ? { from: sha(from), to: sha(to - 1), count: to - from, latest: to === m.commits.length } : null,
    reader: open && {
      path: open.node.path,
      commit: open.commit === null ? null : open.commit === 'working' ? 'working' : sha(open.commit),
      visible: view.lines.visible,
      selection: view.lines.selection,
      text: view.lines.text,
      removed: view.lines.removed,
    },
    area: view.area,
    fill: view.fill,
    theme: view.theme,
  }
}
