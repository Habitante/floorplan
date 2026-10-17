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

/**
 * For every node, the committer time (Unix seconds) of the newest commit in commits[0, to) that
 * touches it or anything beneath it; 0 when none does.
 */
export function lastCommitTimes(model: Model, to: number): Float64Array {
  const times = new Float64Array(model.nodes.length)
  // Newest first: the first commit to reach a node is its last change. Climb until an ancestor
  // already has a time; everything above it was reached by a commit at least as recent.
  for (let c = Math.min(to, model.commits.length) - 1; c >= 0; c--) {
    const { time, files } = model.commits[c]
    for (const id of files) {
      for (let node: Node | null = model.byFileId[id] ?? null; node && times[node.index] === 0; node = node.parent) {
        times[node.index] = time
      }
    }
  }
  return times
}

const DAY = 86_400
const YEAR = 365 * DAY
const AGE_LIMITS = [YEAR, 90 * DAY, 30 * DAY, 7 * DAY, DAY]

/** Discrete age steps, the most recent hottest: 5 within a day, 4 a week, 3 thirty days, 2 ninety days, 1 a year; 0 older. */
export function ageStep(time: number, asOf: number): number {
  if (time <= 0) return 0
  const age = asOf - time
  let step = 0
  for (const limit of AGE_LIMITS) if (age <= limit) step++
  return step
}

export const AGE_STEP_LABELS = ['Older', '1 year', '90 days', '30 days', '7 days', '1 day']

/** What the age lens draws from. */
export interface Ages {
  /** Newest commit up to the range's end touching each node; 0 for none. */
  times: Float64Array
  /** Newest commit anywhere in the loaded history touching each node; 0 for none. */
  ever: Float64Array
  /** The moment ages are measured from, Unix seconds. */
  asOf: number
  /** The history was cut short more than a year before asOf, so a node it never touches is older than that. */
  olderThanLoaded: boolean
}

export function ages(model: Model, to: number, asOf: number): Ages {
  const times = lastCommitTimes(model, to)
  const ever = to >= model.commits.length ? times : lastCommitTimes(model, model.commits.length)
  const oldest = model.commits[0]?.time ?? 0
  return { times, ever, asOf, olderThanLoaded: !model.scan.project.historyComplete && oldest > 0 && asOf - oldest > YEAR }
}

/** An age that cannot be told from the loaded history; drawn hatched, like other unmeasured values. */
export const UNKNOWN_AGE = -1

/**
 * A node's age step. Without a commit up to the range's end, a node is older than a year only when
 * it has no commit anywhere in a history that was cut short more than a year back. Otherwise its
 * age is unknown: it came after the range's end, was never committed, or its last commit was not
 * loaded.
 */
export function nodeAgeStep(ages: Ages, node: Node): number {
  const time = ages.times[node.index]
  if (time > 0) return ageStep(time, ages.asOf)
  const change = node.src.change
  const uncommitted = node.isFile && (change === 'added' || change === 'untracked' || change === 'renamed')
  return ages.olderThanLoaded && ages.ever[node.index] === 0 && !uncommitted ? 0 : UNKNOWN_AGE
}
