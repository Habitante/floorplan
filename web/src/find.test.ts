import { describe, expect, it } from 'vitest'
import { find, fold, terms } from './find'
import { buildModel, weightOf } from './model'
import { applyScan, focus, footprint, jumpTo, model, reader, selected, selectedCommit } from './state'
import type { FileKind, Scan, ScanNode } from './types'

/** A scan holding the given files, with their folders; files are code with one line unless given. */
function scanOf(files: { path: string; kind?: FileKind; lines?: number }[], commits: Scan['commits'] = []): Scan {
  const root: ScanNode = { name: 'p', path: '', children: [] }
  files.forEach((f, id) => {
    const parts = f.path.split('/')
    let folder = root
    for (let i = 0; i < parts.length - 1; i++) {
      const path = parts.slice(0, i + 1).join('/')
      let next = folder.children!.find(c => c.path === path)
      if (!next) folder.children!.push((next = { name: parts[i], path, children: [] }))
      folder = next
    }
    const kind = f.kind ?? 'code'
    const lines = kind === 'asset' ? undefined : f.lines ?? 1
    folder.children!.push({ name: parts.at(-1)!, path: f.path, id, kind, status: 'ok', lines, bytes: 10 })
  })
  // Folder totals as the scan gives them: code lines (all these files are code or assets), files and bytes.
  const total = (n: ScanNode): ScanNode => {
    if (n.id !== undefined) return n
    const kids = n.children!.map(total)
    const add = (of: (c: ScanNode) => number) => kids.reduce((s, c) => s + of(c), 0)
    return {
      ...n,
      children: kids,
      files: add(c => (c.id !== undefined ? 1 : c.files!)),
      code: add(c => (c.id !== undefined ? (c.kind === 'code' ? c.lines! : 0) : c.code!)),
      bytes: add(c => c.bytes!),
    }
  }
  return {
    version: 1,
    project: { root: `C:\\finder\\${files.length}`, name: 'p', scope: 'tracked', git: true, commitLimit: 1000, historyComplete: true, changes: 0, untrackedHidden: 0, scannedAt: '', durationMs: 0 },
    tree: total(root),
    commits,
    issues: [],
  }
}

const paths = (list: string[]) => scanOf(list.map(path => ({ path })))
const best = (scan: Scan, query: string) => find(buildModel(scan), query).found.map(f => f.node.path)

describe('the file finder', () => {
  it('matches characters in order, ignoring case', () => {
    const scan = paths(['src/McpCommand.cs', 'src/Program.cs', 'web/src/main.tsx'])
    expect(best(scan, 'mcpcom')).toEqual(['src/McpCommand.cs'])
    expect(best(scan, 'MAIN')).toEqual(['web/src/main.tsx'])
    expect(best(scan, 'commandmcp')).toEqual([])
  })

  it('lists folders as well as files', () => {
    expect(best(paths(['scenes/world/player.gd', 'tools/x.py']), 'world')).toEqual(['scenes/world', 'scenes/world/player.gd'])
  })

  it('prefers a match in the name over one in the folders', () => {
    const order = best(paths(['scenes/player/hud.gd', 'scenes/world/player.gd']), 'player')
    // The folder named player is an exact name, so it leads.
    expect(order).toEqual(['scenes/player', 'scenes/world/player.gd', 'scenes/player/hud.gd'])
  })

  it('prefers a whole name, then the term at a word start, then the term whole, over word starts and scattered letters', () => {
    const scan = paths(['d/pxlxaxyxexr.gd', 'c/plain_layer.gd', 'b/multiplayer.gd', 'scenes/world/player.gd', 'a/player_controller.gd'])
    expect(best(scan, 'player')).toEqual(['scenes/world/player.gd', 'a/player_controller.gd', 'b/multiplayer.gd', 'c/plain_layer.gd', 'd/pxlxaxyxexr.gd'])
  })

  it('rewards capitals and separators as word starts', () => {
    expect(best(paths(['src/BoardTools.cs', 'src/abort.cs']), 'bt')[0]).toBe('src/BoardTools.cs')
  })

  it('needs every space-separated term to match, anywhere in the path', () => {
    const scan = paths(['tests/test_players.gd', 'scenes/player.gd', 'tests/test_enemy.gd'])
    expect(best(scan, 'test player')).toEqual(['tests/test_players.gd'])
    expect(best(scan, 'player test')).toEqual(['tests/test_players.gd'])
  })

  it('reads a backslash as a slash, and ignores accents and spaces in names', () => {
    const scan = paths(['docs/Diseño del mundo.md', 'scenes/world/player.gd'])
    expect(best(scan, 'world\\player')).toEqual(['scenes/world/player.gd'])
    expect(best(scan, 'diseno mundo')).toEqual(['docs/Diseño del mundo.md'])
  })

  it('keeps one folded character per character, so marks land on the right letters', () => {
    expect(fold('İstanbul/Ünïcode')).toHaveLength('İstanbul/Ünïcode'.length)
    expect(terms('  Foo  bar ')).toEqual(['foo', 'bar'])
    const [hit] = find(buildModel(paths(['src/BoardTools.cs'])), 'bt').found
    expect(hit.positions.map(i => hit.node.path[i]).join('')).toBe('BT')
  })

  it('breaks ties by the shorter path, and counts every match beyond the limit', () => {
    const many = Array.from({ length: 80 }, (_, i) => `f/${'x'.repeat(i % 7)}file${i}.gd`)
    const { found, total } = find(buildModel(paths(many)), 'file', 50)
    expect(found).toHaveLength(50)
    expect(total).toBe(80)
    expect(found[0].node.path.length).toBeLessThanOrEqual(found[49].node.path.length)
  })

  it('puts what has area first among equal matches', () => {
    const scan = scanOf([{ path: 'scenes/heat_haze.gd.uid', kind: 'data' }, { path: 'shaders/heat_haze.gdshader' }])
    const m = buildModel(scan)
    expect(find(m, 'heat haze').found.map(f => f.node.path)).toEqual(['scenes/heat_haze.gd.uid', 'shaders/heat_haze.gdshader'])
    expect(find(m, 'heat haze', 50, weightOf('lines')).found.map(f => f.node.path)).toEqual(['shaders/heat_haze.gdshader', 'scenes/heat_haze.gd.uid'])
  })

  it('leaves an empty query empty', () => {
    expect(find(buildModel(paths(['a.gd'])), '   ')).toEqual({ found: [], total: 0 })
  })
})

describe('choosing in the finder', () => {
  const project = scanOf(
    [
      { path: 'scenes/world/player.gd', lines: 50 },
      { path: 'scenes/world/npc.gd', lines: 20 },
      { path: 'scenes/hud.gd', lines: 10 },
      { path: 'art/textures/stone.png', kind: 'asset' },
      { path: 'scenes/world/icon.png', kind: 'asset' },
    ],
    [{ sha: 'c1', time: 1, author: 't', subject: 's', files: [0], other: 0 }],
  )
  const node = (path: string) => model.value!.byPath.get(path)!

  it('reads a file in its folder', () => {
    footprint.value = 'lines'
    applyScan(project)
    jumpTo(node('scenes/world/player.gd'))
    expect(focus.value?.path).toBe('scenes/world')
    expect(selected.value?.path).toBe('scenes/world/player.gd')
    expect(reader.value).toMatchObject({ node: node('scenes/world/player.gd'), commit: null })
  })

  it('reads the diff of a selected commit that touched the file, and the file otherwise', () => {
    applyScan(project)
    selectedCommit.value = 0
    jumpTo(node('scenes/world/player.gd'))
    expect(reader.value?.commit).toBe(0)
    jumpTo(node('scenes/hud.gd'))
    expect(reader.value?.commit).toBeNull()
  })

  it('zooms into a folder and closes the reader', () => {
    applyScan(project)
    jumpTo(node('scenes/hud.gd'))
    jumpTo(node('scenes/world'))
    expect(focus.value?.path).toBe('scenes/world')
    expect(selected.value).toBeNull()
    expect(reader.value).toBeNull()
  })

  it('marks something without area on the nearest folder that has area', () => {
    applyScan(project)
    footprint.value = 'lines'
    // The asset has no lines, its folder has: the folder is drawn in its parent's view.
    jumpTo(node('scenes/world/icon.png'))
    expect(focus.value?.path).toBe('scenes')
    expect(selected.value?.path).toBe('scenes/world/icon.png')
    // A folder of assets only: the view is the project, where nothing above it has area.
    jumpTo(node('art/textures'))
    expect(focus.value?.path).toBe('')
    expect(selected.value?.path).toBe('art/textures')
    footprint.value = 'bytes'
    jumpTo(node('art/textures'))
    expect(focus.value?.path).toBe('art/textures')
    footprint.value = 'lines'
  })
})
