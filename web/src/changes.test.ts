import { describe, expect, it } from 'vitest'
import { arrivedPaths, changeCounts, changeKind, changeMark, changeSummary, changeWords, describeChange } from './changes'
import { buildModel } from './model'
import { names, regionName } from './since'
import type { Scan, ScanNode, WorkingChange } from './types'

const file = (path: string, id: number, change?: WorkingChange, extra: Partial<ScanNode> = {}): ScanNode =>
  ({ name: path.split('/').pop()!, path, id, kind: 'code', status: change === 'deleted' ? 'missing' : 'ok', lines: 1, bytes: 10, change, ...extra })

function scanOf(files: ScanNode[]): Scan {
  const folders = new Map<string, ScanNode[]>()
  for (const f of files) {
    const folder = f.path.split('/')[0]
    folders.set(folder, [...(folders.get(folder) ?? []), f])
  }
  const children = [...folders].map(([name, list]) => ({ name, path: name, files: list.length, code: list.length, bytes: 10 * list.length, children: list }))
  return {
    version: 1,
    project: { root: 'C:\\p', name: 'p', scope: 'tracked+untracked', git: true, commitLimit: 1000, historyComplete: true, changes: 0, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
    tree: { name: 'p', path: '', files: files.length, code: files.length, bytes: 10 * files.length, children },
    commits: [],
    issues: [],
  }
}

const before = scanOf([
  file('a/x.gd', 0, 'modified'),
  file('a/kept.gd', 1, 'untracked'),
  file('b/y.gd', 2),
  file('b/gone.gd', 3, 'deleted', { bytes: undefined, lines: undefined }),
])
const after = scanOf([
  ...before.tree.children!.flatMap(c => c.children!),
  file('a/new.gd', 4, 'untracked'),
  file('a/moved.gd', 5, 'renamed', { renamedFrom: 'b/old.gd' }),
])

describe('uncommitted changes on the map', () => {
  it('count a rename or an addition as new at its path', () => {
    expect((['untracked', 'added', 'renamed', 'modified', 'deleted'] as const).map(changeKind)).toEqual(['new', 'new', 'new', 'modified', 'deleted'])
  })

  it('are counted per folder, deleted files included, as the region headers show them', () => {
    const m = buildModel(after)
    const at = (path: string) => changeCounts(m, m.byPath.get(path)!)
    expect(at('a')).toEqual({ new: 3, modified: 1, deleted: 0 })
    expect(changeSummary(at('a'))).toBe('+3 ~1')
    expect(changeSummary(at('b'))).toBe('−1')
    expect(changeSummary(at(''))).toBe('+3 ~1 −1')
    expect(changeWords(at(''))).toBe('3 new, 1 modified, 1 deleted')
    expect(changeSummary(at('b/y.gd'))).toBe('')
  })

  it('use three words and letters, and name a renamed file’s old path', () => {
    const m = buildModel(after)
    expect(describeChange(m.byPath.get('a/moved.gd')!)).toBe('New, uncommitted, renamed from b/old.gd')
    expect(describeChange(m.byPath.get('a/kept.gd')!)).toBe('New, uncommitted')
    expect(describeChange(m.byPath.get('b/gone.gd')!)).toBe('Deleted, uncommitted')
    expect(describeChange(m.byPath.get('b/y.gd')!)).toBeNull()
    expect((['untracked', 'added', 'renamed', 'modified', 'deleted'] as const).map(changeMark).join('')).toBe('NNNMD')
  })

  it('pulse only for new files a refresh brings', () => {
    expect(arrivedPaths(buildModel(before), buildModel(after))).toEqual(['a/moved.gd', 'a/new.gd'])
    expect(arrivedPaths(buildModel(after), buildModel(after))).toEqual([])
  })

  it('group the top level as one region in the briefing', () => {
    expect(names([{ name: '', commits: 2 }, { name: 'src', commits: 1 }], 3, regionName)).toBe('top level, src')
  })
})
