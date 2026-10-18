import { describe, expect, it } from 'vitest'
import { nodeAgeStep, UNKNOWN_AGE } from './activity'
import { age, applyScan, model, range } from './state'
import type { Scan, ScanNode } from './types'

const DAY = 86_400
/** November 2023: years before any run of these tests. */
const NEWEST = 1_700_000_000

const file = (path: string, id: number): ScanNode =>
  ({ name: path, path, id, kind: 'code', status: 'ok', lines: 1, bytes: 10 })

const scan: Scan = {
  version: 1,
  project: { root: 'C:\\age', name: 'p', scope: 'tracked', git: true, commitLimit: 1000, historyComplete: true, changes: 0, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
  tree: { name: 'p', path: '', files: 2, code: 2, bytes: 20, children: [file('a.gd', 0), file('b.gd', 1)] },
  // Newest first, as Git lists them: b.gd now, a.gd sixty days earlier.
  commits: [
    { sha: 'bb2', time: NEWEST, author: 't', subject: 'b', files: [1], other: 0 },
    { sha: 'aa1', time: NEWEST - 60 * DAY, author: 't', subject: 'a', files: [0], other: 0 },
  ],
  issues: [],
}

describe('the age lens', () => {
  it("measures from the range's newest commit, not from now", () => {
    applyScan(scan)
    const node = (path: string) => model.value!.byPath.get(path)!
    expect(age.value!.asOf).toBe(NEWEST)
    // Within a day of the newest commit, though years before today.
    expect(nodeAgeStep(age.value!, node('b.gd'))).toBe(5)
    // Sixty days before it: the 90-day step.
    expect(nodeAgeStep(age.value!, node('a.gd'))).toBe(2)

    // Moving the range's end back measures from the commit it ends at.
    range.value = [0, 1]
    expect(age.value!.asOf).toBe(NEWEST - 60 * DAY)
    expect(nodeAgeStep(age.value!, node('a.gd'))).toBe(5)
    expect(nodeAgeStep(age.value!, node('b.gd'))).toBe(UNKNOWN_AGE)
  })
})
