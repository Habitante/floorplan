import type { Commit, FileKind, Scan, ScanNode } from './types'

export interface Node {
  /** Dense index over all nodes, for per-node arrays. */
  readonly index: number
  readonly name: string
  readonly path: string
  readonly parent: Node | null
  readonly children: Node[]
  readonly depth: number
  readonly src: ScanNode
  readonly isFile: boolean
  readonly kind: FileKind | null
  /** Measured bytes; unavailable sizes contribute nothing. */
  readonly bytes: number
  /** Physical lines in code and documentation files. */
  readonly lines: number
  readonly files: number
}

export interface Model {
  readonly scan: Scan
  readonly root: Node
  readonly nodes: Node[]
  readonly byFileId: Node[]
  readonly byPath: Map<string, Node>
  /** Oldest first, for the timeline. */
  readonly commits: Commit[]
  /** Files with uncommitted changes, by path. */
  readonly changed: Node[]
  /** Per node and kind (KINDS order): lines and bytes beneath it. Index = node.index * KINDS.length + kind. */
  readonly kindLines: Float64Array
  readonly kindBytes: Float64Array
}

export type Footprint = 'lines' | 'bytes'

export const KINDS: readonly FileKind[] = ['code', 'docs', 'data', 'asset', 'other']

export function buildModel(scan: Scan): Model {
  const nodes: Node[] = []
  const byFileId: Node[] = []
  const byPath = new Map<string, Node>()

  const visit = (src: ScanNode, parent: Node | null, depth: number): Node => {
    const isFile = src.id !== undefined
    const node: Node = {
      index: nodes.length,
      name: src.name,
      path: src.path,
      parent,
      children: [],
      depth,
      src,
      isFile,
      kind: src.kind ?? null,
      bytes: src.bytes ?? 0,
      lines: isFile
        ? src.kind === 'code' || src.kind === 'docs' ? src.lines ?? 0 : 0
        : (src.code ?? 0) + (src.docs ?? 0),
      files: isFile ? 1 : src.files ?? 0,
    }
    nodes.push(node)
    byPath.set(node.path, node)
    if (isFile) byFileId[src.id!] = node
    for (const child of src.children ?? []) node.children.push(visit(child, node, depth + 1))
    return node
  }

  const root = visit(scan.tree, null, 0)

  const kindLines = new Float64Array(nodes.length * KINDS.length)
  const kindBytes = new Float64Array(nodes.length * KINDS.length)
  for (const node of nodes) {
    if (!node.isFile) continue
    const k = KINDS.indexOf(node.kind ?? 'other')
    for (let n: Node | null = node; n; n = n.parent) {
      kindLines[n.index * KINDS.length + k] += node.src.lines ?? 0
      kindBytes[n.index * KINDS.length + k] += node.bytes
    }
  }

  const changed = nodes.filter(n => n.isFile && n.src.change).sort((a, b) => (a.path < b.path ? -1 : 1))
  return { scan, root, nodes, byFileId, byPath, commits: [...scan.commits].reverse(), changed, kindLines, kindBytes }
}

/** The kind carrying most of a node's weight under the footprint. */
export function dominantKind(model: Model, node: Node, footprint: Footprint): FileKind {
  if (node.kind) return node.kind
  const table = footprint === 'lines' ? model.kindLines : model.kindBytes
  let best = KINDS.length - 1
  let bestWeight = -1
  // Under the lines footprint only code and docs carry weight.
  const candidates = footprint === 'lines' ? [0, 1] : [0, 1, 2, 3, 4]
  for (const k of candidates) {
    const w = table[node.index * KINDS.length + k]
    if (w > bestWeight) {
      best = k
      bestWeight = w
    }
  }
  return KINDS[best]
}

export const weightOf = (footprint: Footprint) =>
  footprint === 'lines' ? (n: Node) => n.lines : (n: Node) => n.bytes

export function ancestry(node: Node): Node[] {
  const path: Node[] = []
  for (let n: Node | null = node; n; n = n.parent) path.unshift(n)
  return path
}

export function contains(ancestor: Node, node: Node): boolean {
  for (let n: Node | null = node; n; n = n.parent) if (n === ancestor) return true
  return false
}

export function displayName(model: Model, node: Node): string {
  return node === model.root ? model.scan.project.name : node.name
}
