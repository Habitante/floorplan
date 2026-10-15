import type { Node } from './model'
import { squarify, type Rect } from './squarify'

export interface Item extends Rect {
  node: Node
  /** Depth below the focused folder. */
  depth: number
  /** True when this folder's children were laid out inside it. */
  open: boolean
  /** Height of the folder's label strip, 0 when there is no room. */
  header: number
}

export const HEADER = 16
const PAD = 2
const MIN_INNER = 3

/**
 * Nested squarified layout of `focus` inside `bounds`. Items are ordered parents first, so
 * drawing in order paints children over their folder and hit-testing in reverse finds the
 * deepest visible item. Folders too small to hold their children are left closed.
 */
export function layoutTree(focus: Node, bounds: Rect, weight: (n: Node) => number): Item[] {
  const items: Item[] = []

  const visit = (node: Node, r: Rect, depth: number) => {
    const item: Item = { ...r, node, depth, open: false, header: 0 }
    items.push(item)
    if (node.isFile) return

    let header = 0
    let pad = 0
    if (depth > 0) {
      header = r.h >= HEADER * 2.5 && r.w >= 40 ? HEADER : 0
      pad = r.w > 10 && r.h > 10 ? PAD : 0
    }
    const inner: Rect = {
      x: r.x + pad,
      y: r.y + (header || pad),
      w: r.w - 2 * pad,
      h: r.h - (header || pad) - pad,
    }
    if (inner.w < MIN_INNER || inner.h < MIN_INNER) return

    const children = node.children
      .map(child => ({ child, w: weight(child) }))
      .filter(c => c.w > 0)
      .sort((a, b) => b.w - a.w || (a.child.name < b.child.name ? -1 : a.child.name > b.child.name ? 1 : 0))
    if (children.length === 0) return

    item.open = true
    item.header = header
    const rects = squarify(children.map(c => c.w), inner)
    children.forEach((c, i) => visit(c.child, rects[i], depth + 1))
  }

  visit(focus, bounds, 0)
  return items
}

/** The item drawn for `node`, or for its nearest drawn ancestor. */
export function nearestItem(items: readonly Item[], node: Node): Item | undefined {
  const byNode = new Map<Node, Item>()
  for (const item of items) byNode.set(item.node, item)
  for (let n: Node | null = node; n; n = n.parent) {
    const found = byNode.get(n)
    if (found) return found
  }
  return undefined
}

/** Deepest item under the point. */
export function hitTest(items: readonly Item[], x: number, y: number): Item | undefined {
  for (let i = items.length - 1; i >= 0; i--) {
    const it = items[i]
    if (x >= it.x && y >= it.y && x < it.x + it.w && y < it.y + it.h) return it
  }
  return undefined
}
