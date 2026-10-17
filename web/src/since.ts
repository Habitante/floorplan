import type { Model } from './model'
import type { SinceReport, Tally } from './types'

// Since I was last here: the commits that landed while the human was away, as the host measured
// them. Pure functions; state.ts applies the range and components.tsx draws the chip.

/** Index into model.commits (oldest first) of the briefing's oldest unseen commit; -1 when not loaded. */
export function sinceStart(m: Model, since: SinceReport | null): number {
  const from = since?.from
  if (!from) return -1
  for (let i = m.commits.length - 1; i >= 0; i--) if (m.commits[i].sha.startsWith(from)) return i
  return -1
}

/** The range a briefing opens: the commits since, and any that landed after them. */
export function sinceRange(m: Model, since: SinceReport | null): [number, number] | null {
  const start = sinceStart(m, since)
  return start >= 0 ? [start, m.commits.length] : null
}

export function onSince(m: Model, since: SinceReport | null, range: readonly [number, number]): boolean {
  const target = sinceRange(m, since)
  return target !== null && target[0] === range[0] && target[1] === range[1]
}

/** The first few names, and how many more. */
export function names(tallies: readonly Tally[] | undefined, limit: number, label = (name: string) => name): string {
  if (!tallies?.length) return ''
  const shown = tallies.slice(0, limit).map(t => label(t.name)).join(', ')
  return tallies.length > limit ? `${shown} +${tallies.length - limit}` : shown
}

/** A region as the note names it: files at the top level count as one region, "". */
export const regionName = (name: string) => (name === '' ? 'top level' : name)

/** Who made the commits: their authors, with the agents named as co-authors. */
export function people(since: SinceReport): string {
  const authors = names(since.authors, 2)
  const agents = names(since.coAuthors, 2)
  return authors && agents ? `${authors} with ${agents}` : authors || agents
}
