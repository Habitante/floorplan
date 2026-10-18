import { describe, expect, it } from 'vitest'
import { buildModel } from './model'
import { relatedGroups, relatedNodes, tier } from './relations'
import type { FileKind, FileRelations, Partner, Scan, ScanNode } from './types'

const file = (path: string, id: number, kind: FileKind = 'code'): ScanNode =>
  ({ name: path.split('/').pop()!, path, id, kind, status: 'ok', lines: 1, bytes: 10 })

const scan: Scan = {
  version: 1,
  project: { root: 'C:\\rel', name: 'p', scope: 'tracked', git: true, commitLimit: 1000, historyComplete: true, changes: 0, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
  tree: {
    name: 'p', path: '', files: 5, code: 5, bytes: 50, children: [
      { name: 'scenes', path: 'scenes', files: 3, code: 3, bytes: 30, children: [file('scenes/player.gd', 0), file('scenes/glow.gdshader', 1), file('scenes/enemy.gd', 2)] },
      { name: 'tests', path: 'tests', files: 2, tests: 2, bytes: 20, children: [file('tests/test_player.gd', 3, 'test'), file('tests/test_enemy.gd', 4, 'test')] },
    ],
  },
  commits: [],
  issues: [],
}
const m = buildModel(scan)
const node = (path: string) => m.byPath.get(path)!
const partner = (path: string, strength: number, together = 2): Partner => ({ path, together, commits: 3, strength, shared: ['abc'] })

describe('files that change together', () => {
  const player: FileRelations = {
    path: 'scenes/player.gd',
    commits: 3,
    changesWith: [partner('scenes/glow.gdshader', 0.8), partner('tests/test_enemy.gd', 0.4), partner('tests/test_player.gd', 0.3), partner('scenes/enemy.gd', 0.1)],
    pairedByName: ['tests/test_player.gd'],
  }

  it("lists a code file's tests apart, by name first, then the rest", () => {
    const { paired, others } = relatedGroups(m, node('scenes/player.gd'), player)
    expect(paired.map(x => [x.node.path, x.byName, x.partner?.strength ?? null])).toEqual([
      ['tests/test_player.gd', true, 0.3],
      ['tests/test_enemy.gd', false, 0.4],
    ])
    expect(others.map(x => x.node.path)).toEqual(['scenes/glow.gdshader', 'scenes/enemy.gd'])
  })

  it('lists for a test the code it is named for, and its other partners together', () => {
    const r: FileRelations = { path: 'tests/test_player.gd', commits: 2, changesWith: [partner('scenes/player.gd', 0.3), partner('tests/test_enemy.gd', 0.2)], pairedByName: ['scenes/player.gd'] }
    const { paired, others } = relatedGroups(m, node('tests/test_player.gd'), r)
    expect(paired.map(x => x.node.path)).toEqual(['scenes/player.gd'])
    expect(others.map(x => x.node.path)).toEqual(['tests/test_enemy.gd'])
  })

  it('shows nothing for relations of another file', () => {
    expect(relatedGroups(m, node('scenes/enemy.gd'), player)).toEqual({ paired: [], others: [] })
  })

  it('outlines stronger ties more heavily, and a pair by name alone faintest', () => {
    expect([0.9, 0.5, 0.3, 0.25, 0.1].map(tier)).toEqual([3, 3, 2, 2, 1])
    const weights = relatedNodes(m, { ...player, changesWith: player.changesWith.filter(p => p.path !== 'tests/test_player.gd') })
    expect([...weights].map(([n, w]) => [n.path, w])).toEqual([
      ['tests/test_player.gd', 1],
      ['scenes/glow.gdshader', 3],
      ['tests/test_enemy.gd', 2],
      ['scenes/enemy.gd', 1],
    ])
  })
})
