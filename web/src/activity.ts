import type { Model, Node } from './model'

/**
 * For every node, the number of commits in commits[from, to) that touch it or anything
 * beneath it. A commit counts once per node however many of its files it touches.
 */
export function countActivity(model: Model, from: number, to: number): Int32Array {
  const counts = new Int32Array(model.nodes.length)
  const stamp = new Int32Array(model.nodes.length).fill(-1)
  for (let c = Math.max(0, from); c < Math.min(to, model.commits.length); c++) {
    for (const id of model.commits[c].files) {
      // Climb until an ancestor already counted this commit; everything above it has too.
      for (let node: Node | null = model.byFileId[id] ?? null; node && stamp[node.index] !== c; node = node.parent) {
        stamp[node.index] = c
        counts[node.index]++
      }
    }
  }
  return counts
}

/** Marks the files a commit touched and every folder containing them. */
export function markCommit(model: Model, commit: number): Uint8Array {
  const marks = new Uint8Array(model.nodes.length)
  for (const id of model.commits[commit]?.files ?? []) {
    for (let node: Node | null = model.byFileId[id] ?? null; node && !marks[node.index]; node = node.parent) marks[node.index] = 1
  }
  return marks
}

/** Marks the given nodes and every folder containing them. */
export function markNodes(model: Model, nodes: readonly Node[]): Uint8Array {
  const marks = new Uint8Array(model.nodes.length)
  for (const start of nodes) {
    for (let node: Node | null = start; node && !marks[node.index]; node = node.parent) marks[node.index] = 1
  }
  return marks
}

/** Discrete activity steps: 0, 1, 2–3, 4–7, 8–15, 16+. */
export function activityStep(count: number): number {
  if (count <= 0) return 0
  if (count === 1) return 1
  if (count <= 3) return 2
  if (count <= 7) return 3
  if (count <= 15) return 4
  return 5
}

export const ACTIVITY_STEP_LABELS = ['0', '1', '2–3', '4–7', '8–15', '16+']
