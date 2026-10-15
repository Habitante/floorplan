import { describe, expect, it } from 'vitest'
import { countActivity, markCommit, markNodes } from './activity'
import { layoutTree, hitTest, nearestItem } from './layout'
import { buildModel, weightOf } from './model'
import { squarify, type Rect } from './squarify'
import type { Scan, ScanNode } from './types'

const area = (r: Rect) => r.w * r.h

describe('squarify', () => {
  const bounds = { x: 10, y: 20, w: 400, h: 300 }
  const weights = [50, 30, 10, 6, 3, 1]
  const rects = squarify(weights, bounds)

  it('gives each item area proportional to its weight', () => {
    const total = weights.reduce((a, b) => a + b)
    rects.forEach((r, i) => expect(area(r)).toBeCloseTo((weights[i] / total) * area(bounds), 6))
  })

  it('stays inside the bounds without overlap', () => {
    for (const r of rects) {
      expect(r.x).toBeGreaterThanOrEqual(bounds.x - 1e-9)
      expect(r.y).toBeGreaterThanOrEqual(bounds.y - 1e-9)
      expect(r.x + r.w).toBeLessThanOrEqual(bounds.x + bounds.w + 1e-9)
      expect(r.y + r.h).toBeLessThanOrEqual(bounds.y + bounds.h + 1e-9)
    }
    for (let i = 0; i < rects.length; i++)
      for (let j = i + 1; j < rects.length; j++) {
        const a = rects[i], b = rects[j]
        const ox = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x)
        const oy = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y)
        expect(ox > 1e-6 && oy > 1e-6).toBe(false)
      }
  })

  it('keeps rectangles reasonably square', () => {
    const worst = Math.max(...rects.slice(0, 3).map(r => Math.max(r.w / r.h, r.h / r.w)))
    expect(worst).toBeLessThan(3)
  })
})

function scanOf(tree: ScanNode, commits: Scan['commits'] = []): Scan {
  return {
    version: 1,
    project: { root: 'C:\\p', name: 'p', scope: 'tracked', git: true, commitLimit: 1000, historyComplete: true, changes: 0, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
    tree,
    commits,
    issues: [],
  }
}

const file = (path: string, id: number, lines: number, bytes = lines * 10): ScanNode =>
  ({ name: path.split('/').pop()!, path, id, kind: 'code', status: 'ok', lines, bytes })

const tree: ScanNode = {
  name: 'p', path: '', files: 4, code: 160, bytes: 1600, children: [
    { name: 'a', path: 'a', files: 2, code: 110, bytes: 1100, children: [file('a/x.gd', 0, 100), file('a/y.gd', 1, 10)] },
    { name: 'b', path: 'b', files: 1, code: 50, bytes: 500, children: [file('b/z.gd', 2, 50)] },
    file('readme.md', 3, 0),
  ],
}

describe('layoutTree', () => {
  const model = buildModel(scanOf(tree))
  const items = layoutTree(model.root, { x: 0, y: 0, w: 800, h: 600 }, weightOf('lines'))

  it('lays out every weighted file, parents before children', () => {
    expect(items.map(i => i.node.path)).toEqual(['', 'a', 'a/x.gd', 'a/y.gd', 'b', 'b/z.gd'])
  })

  it('omits zero-weight files', () => {
    expect(items.some(i => i.node.path === 'readme.md')).toBe(false)
  })

  it('finds the deepest item under a point and the nearest drawn ancestor', () => {
    const x = items.find(i => i.node.path === 'a/x.gd')!
    expect(hitTest(items, x.x + 1, x.y + 1)?.node.path).toBe('a/x.gd')
    expect(nearestItem(items, model.byPath.get('readme.md')!)?.node).toBe(model.root)
  })

  it('leaves folders without room for their children closed', () => {
    const small = layoutTree(model.root, { x: 0, y: 0, w: 4, h: 3 }, weightOf('lines'))
    expect(small.filter(i => i.depth === 1).map(i => [i.node.path, i.open])).toEqual([['a', false], ['b', false]])
  })
})

describe('activity', () => {
  // Oldest first after buildModel reverses Git's newest-first order.
  const model = buildModel(scanOf(tree, [
    { sha: 'c3', time: 3, author: 't', subject: 'x and y', files: [0, 1], other: 0 },
    { sha: 'c2', time: 2, author: 't', subject: 'z', files: [2], other: 0 },
    { sha: 'c1', time: 1, author: 't', subject: 'x', files: [0], other: 2 },
  ]))
  const count = (path: string, from = 0, to = 3) => countActivity(model, from, to)[model.byPath.get(path)!.index]

  it('counts each commit once per region', () => {
    expect(count('a/x.gd')).toBe(2)
    expect(count('a/y.gd')).toBe(1)
    expect(count('a')).toBe(2) // c3 touches two files in a/ but counts once
    expect(count('')).toBe(3)
  })

  it('respects the selected range', () => {
    expect(count('a/x.gd', 0, 1)).toBe(1) // only c1
    expect(count('b', 0, 1)).toBe(0)
    expect(count('', 1, 3)).toBe(2)
  })

  it('marks a commit’s files and their folders', () => {
    const marks = markCommit(model, 2) // c3
    const marked = model.nodes.filter(n => marks[n.index]).map(n => n.path)
    expect(marked.sort()).toEqual(['', 'a', 'a/x.gd', 'a/y.gd'])
  })
})

describe('working changes', () => {
  const changedTree: ScanNode = {
    ...tree,
    children: [
      { ...tree.children![0], children: [{ ...file('a/x.gd', 0, 100), change: 'modified' }, file('a/y.gd', 1, 10)] },
      tree.children![1],
      { ...file('readme.md', 3, 0), change: 'untracked' },
    ],
  }
  const model = buildModel(scanOf(changedTree))

  it('lists changed files by path', () => {
    expect(model.changed.map(n => n.path)).toEqual(['a/x.gd', 'readme.md'])
  })

  it('marks changed files and their folders', () => {
    const marks = markNodes(model, model.changed)
    expect(model.nodes.filter(n => marks[n.index]).map(n => n.path).sort()).toEqual(['', 'a', 'a/x.gd', 'readme.md'])
  })
})
