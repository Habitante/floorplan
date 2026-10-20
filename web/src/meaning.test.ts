import { describe, expect, it } from 'vitest'
import { codeShown, splitReference, systemCodes, systemMarks, systemOfFile, systemsOf, systemTotals } from './meaning'
import { buildModel } from './model'
import type { DescriptionReport, Scan, ScanNode } from './types'

const file = (path: string, id: number, lines: number): ScanNode =>
  ({ name: path.split('/').pop()!, path, id, kind: 'code', status: 'ok', lines, bytes: lines * 10 })

const scan: Scan = {
  version: 1,
  project: { root: 'C:\\meaning', name: 'p', scope: 'tracked', git: true, commitLimit: 1000, historyComplete: true, changes: 0, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
  tree: {
    name: 'p', path: '', files: 4, code: 100, bytes: 1000, children: [
      { name: 'core', path: 'core', files: 3, code: 90, bytes: 900, children: [file('core/vm.gd', 0, 60), file('core/grid.gd', 1, 20), file('core/menu.gd', 2, 10)] },
      file('notes.gd', 3, 10),
    ],
  },
  commits: [],
  issues: [],
}
const m = buildModel(scan)
const node = (path: string) => m.byPath.get(path)!
const system = (id: string) => ({ id, name: id, paths: [], docs: [] })
const report: DescriptionReport = {
  file: 'd.json',
  description: { floorplan: 1, systems: [system('shared'), system('ui')], direction: [], findings: [] },
  systemOf: [0, 1, 1, -1],
}

describe('systems on the map', () => {
  it("gives files their own system and folders the one with most weight", () => {
    const of = systemsOf(m, report, 'lines')
    expect(of[node('core/vm.gd').index]).toBe(0)
    expect(of[node('core/menu.gd').index]).toBe(1)
    expect(of[node('notes.gd').index]).toBe(-1)
    // vm.gd's 60 lines outweigh the 30 of the UI's two files.
    expect(of[node('core').index]).toBe(0)
    expect(systemOfFile(report, node('core/grid.gd'))?.id).toBe('ui')
    expect(systemOfFile(report, node('notes.gd'))).toBeNull()
  })

  it('gives a folder no system when the systems hold half of it or less', () => {
    // Only grid.gd (20 of the folder's 90 lines) is named: core is no system's.
    const sparse: DescriptionReport = { ...report, systemOf: [-1, 1, -1, -1] }
    const of = systemsOf(m, sparse, 'lines')
    expect(of[node('core').index]).toBe(-1)
    expect(of[node('core/grid.gd').index]).toBe(1)
    expect(codeShown(of, node('core/grid.gd'), 2)).toBe(1)
  })

  it('lights a picked system with the folders that hold it', () => {
    const marks = systemMarks(m, report, 1)!
    expect(['core/grid.gd', 'core/menu.gd', 'core', ''].map(p => marks[node(p).index])).toEqual([1, 1, 1, 1])
    expect(['core/vm.gd', 'notes.gd'].map(p => marks[node(p).index])).toEqual([0, 0])
    expect(systemMarks(m, report, null)).toBeNull()
  })

  it('totals each system', () => {
    expect(systemTotals(m, report)).toEqual([{ files: 1, lines: 60, bytes: 600 }, { files: 2, lines: 30, bytes: 300 }])
  })

  it('prints a code per system: its own, else one from its name, kept unique', () => {
    const named = (id: string, name: string, code?: string) => ({ ...system(id), name, code })
    const r: DescriptionReport = {
      file: 'd.json',
      description: {
        floorplan: 1, direction: [], findings: [],
        systems: [named('a', 'The 3D view'), named('b', 'Save cache'), named('c', 'Interface', ' ui '), named('d', 'Shared cache')],
      },
    }
    expect(systemCodes(r)).toEqual(['3D', 'SC', 'ui', 'SC2'])
  })

  it('prints a code on a region, and deeper only where it differs from the folder around it', () => {
    const of = systemsOf(m, report, 'lines')
    expect(codeShown(of, node('core'), 1)).toBe(0)
    expect(codeShown(of, node('core/vm.gd'), 2)).toBe(-1)
    expect(codeShown(of, node('core/grid.gd'), 2)).toBe(1)
    expect(codeShown(of, node('notes.gd'), 1)).toBe(-1)
    // Zoomed into core, its files are regions of their own view.
    expect(codeShown(of, node('core/vm.gd'), 1)).toBe(0)
  })

  it('reads references with lines as agents write them', () => {
    expect(splitReference('docs/ROADMAP.md:18-19')).toEqual({ path: 'docs/ROADMAP.md', from: 18, to: 19 })
    expect(splitReference('docs\\ROADMAP.md:64')).toEqual({ path: 'docs/ROADMAP.md', from: 64, to: 64 })
    expect(splitReference('scenes/player.gd')).toEqual({ path: 'scenes/player.gd' })
  })
})
