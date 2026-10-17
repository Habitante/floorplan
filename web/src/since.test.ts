import { describe, expect, it } from 'vitest'
import { names, onSince, people, sinceRange, sinceStart } from './since'
import { applyScan, model, range, reader, receiveSince, selected, since, toggleSince } from './state'
import type { Scan, ScanNode, SinceReport } from './types'

const file = (path: string, id: number): ScanNode =>
  ({ name: path.split('/').pop()!, path, id, kind: 'code', status: 'ok', lines: 1, bytes: 10 })

/** A project whose commits are given oldest first, each touching one file. */
function scanOf(root: string, shas: string[]): Scan {
  return {
    version: 1,
    project: { root, name: 'p', scope: 'tracked', git: true, commitLimit: 1000, historyComplete: true, changes: 0, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
    tree: { name: 'p', path: '', files: 2, code: 2, bytes: 20, children: [file('x.gd', 0), file('y.gd', 1)] },
    commits: shas.map((sha, i) => ({ sha, time: i, author: 't', subject: sha, files: [i % 2], other: 0 })).reverse(),
    issues: [],
  }
}

const briefing = (from?: string, extra: Partial<SinceReport> = {}): SinceReport =>
  ({ seenHead: 'seen', seenAt: '2026-10-17T10:00:00Z', commits: 2, from, ...extra })

const SHAS = ['aa1', 'bb2', 'cc3', 'dd4', 'ee5']

describe('since I was last here', () => {
  it('finds the range from the oldest unseen commit to the newest', () => {
    applyScan(scanOf('C:\\find', SHAS))
    const m = model.value!
    expect(sinceStart(m, briefing('dd4'))).toBe(3)
    expect(sinceRange(m, briefing('dd4'))).toEqual([3, 5])
    expect(sinceRange(m, briefing('ff9'))).toBeNull()
    expect(sinceRange(m, briefing(undefined, { problem: 'gone' }))).toBeNull()
    expect(onSince(m, briefing('dd4'), [3, 5])).toBe(true)
    expect(onSince(m, briefing('dd4'), [2, 5])).toBe(false)
  })

  it('opens a project at the commits since, and keeps their start as more land', () => {
    applyScan(scanOf('C:\\open', SHAS), briefing('dd4'))
    expect(range.value).toEqual([3, 5])
    expect(since.value?.from).toBe('dd4')

    applyScan(scanOf('C:\\open', [...SHAS, 'ff6']))
    expect(range.value).toEqual([3, 6])
    expect(since.value?.from).toBe('dd4')
  })

  it('lets any other range that ends at the newest commit slide', () => {
    applyScan(scanOf('C:\\slide', SHAS))
    range.value = [3, 5]
    applyScan(scanOf('C:\\slide', [...SHAS, 'ff6']))
    expect(range.value).toEqual([4, 6])
    expect(since.value).toBeNull()
  })

  it('only offers the commits since in an open window, and toggles between them and the range before', () => {
    applyScan(scanOf('C:\\back', SHAS))
    expect(range.value).toEqual([0, 5])

    // Nothing switches by itself, whatever the human is doing.
    receiveSince(briefing('dd4'))
    expect(range.value).toEqual([0, 5])
    expect(since.value?.from).toBe('dd4')

    toggleSince()
    expect(range.value).toEqual([3, 5])
    toggleSince()
    expect(range.value).toEqual([0, 5])

    // A range dragged elsewhere comes back the same way.
    const m = model.value!
    selected.value = m.byPath.get('x.gd')!
    reader.value = { node: m.byPath.get('y.gd')!, commit: null }
    range.value = [1, 3]
    receiveSince(briefing('ee5'))
    expect(range.value).toEqual([1, 3])
    toggleSince()
    expect(range.value).toEqual([4, 5])
    toggleSince()
    expect(range.value).toEqual([1, 3])
    expect(selected.value?.path).toBe('x.gd')
  })

  it('names the regions and the people briefly', () => {
    const tallies = ['tools', 'scenes', 'docs', 'README.md'].map(name => ({ name, commits: 1 }))
    expect(names(tallies, 3)).toBe('tools, scenes, docs +1')
    expect(names(undefined, 3)).toBe('')
    const both = briefing('x', { authors: [{ name: 'Habitante', commits: 3 }], coAuthors: [{ name: 'Claude Opus 5.5', commits: 2 }] })
    expect(people(both)).toBe('Habitante with Claude Opus 5.5')
    expect(people(briefing('x', { authors: [{ name: 'Ana', commits: 1 }, { name: 'Luis', commits: 1 }] }))).toBe('Ana, Luis')
  })
})
