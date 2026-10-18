import { effect, signal } from '@preact/signals'
import { inHost, request } from './bridge'
import type { Model, Node } from './model'
import { model, selected } from './state'
import type { FileRelations, Partner } from './types'

// Files that change together with the selected file, as the host reads them from the loaded
// history: "changes with", never "depends on". Tests paired with code by name or by changing
// together are listed apart, and pairing is not coverage.

/** The selected file's relations; null for a folder, with no selection, or before the host answers. */
export const relations = signal<FileRelations | null>(null)

/** A related file as the inspector and the reader list it. */
export interface Related {
  node: Node
  /** Shared commits, when they change together. */
  partner: Partner | null
  /** Paired by the test naming conventions. */
  byName: boolean
}

/**
 * The relations in two groups. For code: its tests, by name or by changing together, then the
 * rest. For a test: the code it is named for, then the rest.
 */
export function relatedGroups(m: Model, node: Node, r: FileRelations | null): { paired: Related[]; others: Related[] } {
  if (!r || r.path !== node.path) return { paired: [], others: [] }
  const named = new Set(r.pairedByName)
  const partnerOf = new Map(r.changesWith.map(p => [p.path, p]))
  const isTest = node.kind === 'test'
  const paired: Related[] = []
  for (const path of r.pairedByName) {
    const n = m.byPath.get(path)
    if (n) paired.push({ node: n, partner: partnerOf.get(path) ?? null, byName: true })
  }
  const others: Related[] = []
  for (const p of r.changesWith) {
    const n = m.byPath.get(p.path)
    if (!n || named.has(p.path)) continue
    if (!isTest && n.kind === 'test') paired.push({ node: n, partner: p, byName: false })
    else others.push({ node: n, partner: p, byName: false })
  }
  return { paired, others }
}

/** Outline weight on the map, 3 for the strongest ties; a pair by name alone is the faintest. */
export function tier(strength: number): 1 | 2 | 3 {
  return strength >= 0.5 ? 3 : strength >= 0.25 ? 2 : 1
}

/** The files to outline around the selection, with their weight. */
export function relatedNodes(m: Model, r: FileRelations | null): Map<Node, number> {
  const nodes = new Map<Node, number>()
  if (!r) return nodes
  for (const path of r.pairedByName) {
    const n = m.byPath.get(path)
    if (n) nodes.set(n, 1)
  }
  for (const p of r.changesWith) {
    const n = m.byPath.get(p.path)
    if (n) nodes.set(n, Math.max(nodes.get(n) ?? 0, tier(p.strength)))
  }
  return nodes
}

/** Asks the host for the selected file's relations whenever the selection or the scan changes. */
export function watchRelations(): () => void {
  let asked = 0
  return effect(() => {
    const m = model.value
    const node = selected.value
    const ask = ++asked
    if (!m || !node?.isFile || !inHost) {
      relations.value = null
      return
    }
    // Keep what is shown for the same file while a refresh's answer is on its way.
    if (relations.peek()?.path !== node.path) relations.value = null
    request<{ relations: FileRelations | null }>({ type: 'relations', path: node.path }).then(
      reply => {
        if (ask === asked) relations.value = reply.relations
      },
      () => {},
    )
  })
}
