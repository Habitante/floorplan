import { describe, expect, it } from 'vitest'
import { zoomOut } from './map-controller'
import { applyScan, focus, inside, model, openReader, reveal, selected, selectedPart, zoomInto, zoomTo } from './state'
import type { Scan, ScanNode } from './types'

const file = (path: string, id: number): ScanNode =>
  ({ name: path.split('/').pop()!, path, id, kind: 'code', status: 'ok', lines: 10, bytes: 100 })

const scan: Scan = {
  version: 1,
  project: { root: 'C:\\inside', name: 'p', scope: 'tracked', git: true, commitLimit: 1000, historyComplete: true, changes: 0, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
  tree: {
    name: 'p', path: '', files: 3, code: 30, bytes: 300,
    children: [
      { name: 'scenes', path: 'scenes', files: 2, code: 20, bytes: 200, children: [file('scenes/player.gd', 0), file('scenes/world.gd', 1)] },
      file('main.gd', 2),
    ],
  },
  commits: [],
  issues: [],
}

const node = (path: string) => model.value!.byPath.get(path)!

describe('the map inside a file', () => {
  it('shows a file’s parts inside its folder, with the file selected', () => {
    applyScan(scan)
    zoomInto(node('scenes/player.gd'))
    expect(inside.value?.path).toBe('scenes/player.gd')
    expect(focus.value?.path).toBe('scenes')
    expect(selected.value?.path).toBe('scenes/player.gd')
    // Reading the file, as clicking a part does, keeps its parts in view.
    selectedPart.value = { path: 'scenes/player.gd', id: 'jump', name: 'jump', kind: 'function', from: 3, to: 4 }
    openReader(node('scenes/player.gd'))
    expect(inside.value?.path).toBe('scenes/player.gd')
    expect(selectedPart.value?.id).toBe('jump')
  })

  it('leaves the parts for another file, a folder, or a step out', () => {
    applyScan(scan)
    zoomInto(node('scenes/player.gd'))
    openReader(node('scenes/world.gd'))
    expect(inside.value).toBeNull()

    zoomInto(node('scenes/player.gd'))
    reveal(node('scenes/world.gd'))
    expect(inside.value).toBeNull()

    zoomInto(node('scenes/player.gd'))
    zoomTo(model.value!.root)
    expect(inside.value).toBeNull()
    expect(focus.value?.path).toBe('')

    // Backspace steps out of the file first, back to its folder with the file selected.
    zoomInto(node('scenes/player.gd'))
    zoomOut()
    expect(inside.value).toBeNull()
    expect(focus.value?.path).toBe('scenes')
    expect(selected.value?.path).toBe('scenes/player.gd')
    expect(selectedPart.value).toBeNull()
  })

  it('keeps the file through a refresh, and drops it when the file is gone', () => {
    applyScan(scan)
    zoomInto(node('scenes/player.gd'))
    applyScan({ ...scan })
    expect(inside.value?.path).toBe('scenes/player.gd')
    expect(inside.value).toBe(node('scenes/player.gd'))
    const without = { ...scan, tree: { ...scan.tree, children: [scan.tree.children![1]] } }
    applyScan(without)
    expect(inside.value).toBeNull()
  })
})
