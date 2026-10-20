import { describe, expect, it } from 'vitest'
import { applyPointer, boardView, commonFolder, describeItem, pointedLines, pointedNodes, pulse } from './board'
import { buildModel } from './model'
import { NO_LINES, type Pointer } from './state'
import type { PointerItem, Scan, ScanNode } from './types'

const file = (path: string, id: number, lines: number): ScanNode =>
  ({ name: path.split('/').pop()!, path, id, kind: 'code', status: 'ok', lines, bytes: lines * 10 })

const tree: ScanNode = {
  name: 'p', path: '', files: 4, code: 160, bytes: 1600, children: [
    { name: 'a', path: 'a', files: 3, code: 111, bytes: 1110, children: [
      file('a/x.gd', 0, 100),
      file('a/y.gd', 1, 10),
      { name: 'deep', path: 'a/deep', files: 1, code: 1, bytes: 10, children: [file('a/deep/z.gd', 2, 1)] },
    ] },
    { name: 'b', path: 'b', files: 1, code: 50, bytes: 500, children: [file('b/w.gd', 3, 50)] },
  ],
}

const scan: Scan = {
  version: 1,
  project: { root: 'C:\\p', name: 'p', scope: 'tracked', git: true, commitLimit: 1000, historyComplete: true, changes: 0, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
  tree,
  commits: [
    { sha: 'c3', time: 3, author: 't', subject: 'three', files: [0], other: 0 },
    { sha: 'c2', time: 2, author: 't', subject: 'two', files: [3], other: 0 },
    { sha: 'c1', time: 1, author: 't', subject: 'one', files: [1], other: 0 },
  ],
  issues: [],
}
const model = buildModel(scan)
const node = (path: string) => model.byPath.get(path)!
const pointer = (agent: string, items: (string | PointerItem)[], arrived = 0): Pointer =>
  ({ agent, caption: '', items: items.map(i => (typeof i === 'string' ? { path: i } : i)), at: 0, arrived })

describe('pointers', () => {
  it('keep one per agent, newest last, and clear on an empty list', () => {
    let list = applyPointer([], pointer('Claude', ['a']))
    list = applyPointer(list, pointer('Codex', ['b']))
    list = applyPointer(list, pointer('Claude', ['a/x.gd']))
    expect(list.map(p => [p.agent, p.items.map(i => i.path)])).toEqual([['Codex', ['b']], ['Claude', ['a/x.gd']]])
    expect(applyPointer(list, pointer('Codex', [])).map(p => p.agent)).toEqual(['Claude'])
    expect(applyPointer(list, pointer('Third', ['b']), 2).map(p => p.agent)).toEqual(['Claude', 'Third'])
  })

  it('resolve to nodes still in the scan', () => {
    const nodes = pointedNodes(model, [pointer('A', ['a/x.gd', 'gone.gd', 'b']), pointer('B', ['b'])])
    expect(nodes.map(n => n.path)).toEqual(['a/x.gd', 'b'])
  })

  it('mark line ranges per file, from every agent', () => {
    const list = [
      pointer('A', [{ path: 'a/x.gd', from: 10, to: 20 }, 'a/y.gd']),
      pointer('B', [{ path: 'a/x.gd', from: 40 }]),
    ]
    expect(pointedLines(list, 'a/x.gd')).toEqual([[10, 20], [40, 40]])
    expect(pointedLines(list, 'a/y.gd')).toEqual([])
  })

  it('read back as agents wrote them', () => {
    expect(describeItem({ path: 'a/x.gd' })).toBe('a/x.gd')
    expect(describeItem({ path: 'a/x.gd', from: 4, to: 4 })).toBe('a/x.gd:4')
    expect(describeItem({ path: 'a/x.gd', from: 4, to: 9 })).toBe('a/x.gd:4-9')
    expect(describeItem({ path: 'a', note: 'a/renders/frame.png' })).toBe('a/renders/frame.png')
  })

  it('go to the deepest folder holding everything they name', () => {
    expect(commonFolder([node('a/x.gd')])?.path).toBe('a')
    expect(commonFolder([node('a/x.gd'), node('a/deep/z.gd')])?.path).toBe('a')
    expect(commonFolder([node('a/deep/z.gd'), node('b/w.gd')])?.path).toBe('')
    expect(commonFolder([node('a/deep')])?.path).toBe('a/deep')
    expect(commonFolder([])).toBeNull()
  })

  it('pulse when they arrive, then settle', () => {
    expect(pulse([pointer('A', ['a'], 1000)], 1000)).toBe(1)
    expect(pulse([pointer('A', ['a'], 1000)], 1700)).toBeGreaterThan(0)
    expect(pulse([pointer('A', ['a'], 1000)], 5000)).toBe(0)
    expect(pulse([], 0)).toBe(0)
  })
})

describe('board view', () => {
  const view = (overrides: Partial<Parameters<typeof boardView>[1]> = {}) => boardView(model, {
    focus: model.root,
    selected: null,
    commit: null,
    range: [0, 3],
    reader: null,
    lines: NO_LINES,
    area: 'lines',
    fill: 'activity',
    theme: 'dark',
    ...overrides,
  })

  it('names the range by its commits and says whether it follows the newest', () => {
    // model.commits is oldest first: c1, c2, c3.
    expect(view().range).toEqual({ from: 'c1', to: 'c3', count: 3, latest: true })
    expect(view({ range: [0, 2] }).range).toEqual({ from: 'c1', to: 'c2', count: 2, latest: false })
  })

  it('reports what the human chose by path and commit, with the selected text', () => {
    const v = view({
      focus: node('a'),
      selected: node('a/x.gd'),
      commit: 2,
      reader: { node: node('a/x.gd'), commit: 'working' },
      lines: { visible: [1, 40], selection: [12, 14], text: 'func _ready():', removed: true },
      area: 'bytes',
    })
    expect(v).toEqual({
      focus: 'a',
      selected: 'a/x.gd',
      part: null,
      commit: 'c3',
      range: { from: 'c1', to: 'c3', count: 3, latest: true },
      reader: { path: 'a/x.gd', commit: 'working', visible: [1, 40], selection: [12, 14], text: 'func _ready():', removed: true },
      area: 'bytes',
      fill: 'activity',
      theme: 'dark',
    })
  })

  it('reports the part picked inside the selected file, and only with it', () => {
    const part = { path: 'a/x.gd', id: 'jump', name: 'jump', kind: 'function', from: 3, to: 9 }
    expect(view({ selected: node('a/x.gd'), part }).part).toEqual({ id: 'jump', name: 'jump', kind: 'function', from: 3, to: 9 })
    expect(view({ selected: node('a/y.gd'), part }).part).toBeNull()
  })

  it('has no range without history', () => {
    const plain = buildModel({ ...scan, commits: [] })
    expect(boardView(plain, {
      focus: plain.root, selected: null, commit: null, range: [0, 0], reader: null,
      lines: NO_LINES, area: 'lines', fill: 'kind', theme: 'light',
    }).range).toBeNull()
  })
})
