import { describe, expect, it } from 'vitest'
import { UNKNOWN_AGE } from './activity'
import { buildModel } from './model'
import { findPart, largestParts, partActivity, partAgeStep, partHistory, partsOf, partTimes, type LineHistory, type Part } from './parts'
import { partKind } from './render-parts'
import type { FileOutline, OutlineSymbol } from './skeleton'
import type { Scan } from './types'

const DAY = 86_400
const NEWEST = 1_700_000_000

const symbol = (kind: string, name: string, from: number, to: number, children?: OutlineSymbol[]): OutlineSymbol =>
  ({ id: name, kind, name, from, line: from, to, children })

// A script: comments on top, three constants and a variable, a section of two functions, and a class.
const outline: FileOutline = {
  language: 'gdscript',
  lines: 60,
  folds: [],
  symbols: [
    symbol('constant', 'SPEED', 6, 6), symbol('constant', 'JUMP', 7, 7), symbol('constant', 'GRAVITY', 8, 10),
    symbol('variable', '_air', 12, 12),
    { kind: 'section', name: 'moving', from: 14, line: 14, to: 40, children: [symbol('function', 'walk', 16, 30), symbol('function', 'jump', 32, 40)] },
    symbol('class', 'Step', 42, 60, [symbol('field', 'Step.height', 43, 43), symbol('field', 'Step.width', 44, 44), symbol('function', 'Step._init', 46, 60)]),
  ],
}

const names = (parts: readonly Part[]) => parts.map(p => `${p.kind} ${p.name} ${p.lines}`)

describe("a file's parts", () => {
  it('gathers a level’s data declarations of one kind into a block, and keeps the lines above the first part', () => {
    const tree = partsOf(outline, 'player.gd')

    expect(tree.root.name).toBe('player.gd')
    expect(names(tree.root.children)).toEqual(['variable _air 1', 'section moving 27', 'class Step 19', 'constants constants 5', 'top top of file 5'])
    const step = tree.root.children.find(p => p.name === 'Step')!
    expect(names(step.children)).toEqual(['function Step._init 15', 'fields fields 2'])
    expect(tree.parts.every((p, i) => p.index === i)).toBe(true)
  })

  it('paints declarations as code, data declarations as data, and a document’s sections as documentation', () => {
    const tree = partsOf(outline, 'player.gd')
    const kind = (name: string) => partKind(tree.parts.find(p => p.name === name)!, 'code')
    expect([kind('walk'), kind('SPEED'), kind('constants'), kind('top of file')]).toEqual(['code', 'data', 'data', 'docs'])
    expect(partKind(tree.parts.find(p => p.name === 'walk')!, 'test')).toBe('test')
  })

  it('names its largest declarations and blocks for the tooltip', () => {
    const tree = partsOf(outline, 'player.gd')
    expect(largestParts(tree).map(p => p.name)).toEqual(['walk', 'Step._init', 'jump'])
    expect(findPart(tree, { id: 'jump', from: 0, to: 0 })?.name).toBe('jump')
    expect(findPart(tree, { id: null, from: 6, to: 10 })?.name).toBe('constants')
  })
})

describe("a part's history", () => {
  // Oldest first in the model: the file's first version, a change to walk, a change to jump and a constant.
  const scan: Scan = {
    version: 1,
    project: { root: 'C:\\parts', name: 'p', scope: 'tracked', git: true, commitLimit: 1000, historyComplete: true, changes: 1, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
    tree: { name: 'p', path: '', files: 1, code: 60, bytes: 600, children: [{ name: 'player.gd', path: 'player.gd', id: 0, kind: 'code', status: 'ok', lines: 60, bytes: 600, change: 'modified' }] },
    commits: [
      { sha: 'c3', time: NEWEST, author: 't', subject: 'jump', files: [0], other: 0 },
      { sha: 'c2', time: NEWEST - 30 * DAY, author: 't', subject: 'walk', files: [0], other: 0 },
      { sha: 'c1', time: NEWEST - 200 * DAY, author: 't', subject: 'start', files: [0], other: 0 },
    ],
    issues: [],
  }
  const model = buildModel(scan)
  const lines: LineHistory = {
    commits: [
      { sha: 'c3', lines: [{ from: 35, to: 36, removed: 1 }, { from: 7, to: 7, removed: 1 }] },
      { sha: 'c2', lines: [{ from: 20, to: 19, removed: 2 }] },   // lines removed inside walk
      { sha: 'c1', lines: [{ from: 1, to: 60, removed: 0 }] },
    ],
    // Uncommitted: Step.width rewritten, and a line of walk changed.
    working: [{ from: 44, to: 44, removed: 1 }, { from: 22, to: 22, removed: 1 }],
  }
  const tree = partsOf(outline, 'player.gd')
  const part = (name: string) => tree.parts.find(p => p.name === name)!
  const history = partHistory(tree, lines, model, false)

  it('counts the commits that changed each part, a block those that changed any member', () => {
    const all = partActivity(history, 0, 3)
    expect([all[part('walk').index], all[part('jump').index], all[part('SPEED').index], all[part('JUMP').index]]).toEqual([2, 2, 1, 2])
    expect(all[part('constants').index]).toBe(2)
    expect(all[part('moving').index]).toBe(3)
    // Only the newest commit in the range.
    const latest = partActivity(history, 2, 3)
    expect([latest[part('walk').index], latest[part('jump').index]]).toEqual([0, 1])
  })

  it('dates each part by its last commit up to the range’s end', () => {
    const { times } = partTimes(history, model, 2)
    expect(times[part('walk').index]).toBe(NEWEST - 30 * DAY)
    expect(times[part('jump').index]).toBe(NEWEST - 200 * DAY)     // its change came after the range
    expect(partAgeStep(0, 0, NEWEST, false, 2)).toBe(UNKNOWN_AGE)    // never committed
    expect(partAgeStep(0, 0, NEWEST, true, 0)).toBe(0)                // older than a history cut short
  })

  it('marks what is uncommitted: changed, or every line new', () => {
    expect(history.change[part('walk').index]).toBe(1)
    expect(history.change[part('Step.width').index]).toBe(2)
    expect(history.change[part('fields').index]).toBe(1)
    expect(history.change[part('jump').index]).toBe(0)
    const fresh = partHistory(tree, null, model, true)
    expect(fresh.change.every(c => c === 2)).toBe(true)
  })
})
