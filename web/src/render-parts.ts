import { activityStep, UNKNOWN_AGE } from './activity'
import * as format from './format'
import type { Item } from './layout'
import { prefersDarkText, type Palette } from './palette'
import { isBlock, partAgeStep, type Part } from './parts'
import { CORNER, DARK_TEXT, drawTag, fit, FONT, LIGHT_TEXT, tagWidth, type PriceTag, type View } from './render'
import type { Lens } from './state'
import type { FileKind } from './types'

// The map inside a file: its parts drawn as files are, with the same lenses, outlines and labels.

export interface PartsScene {
  items: readonly Item<Part>[]
  palette: Palette
  lens: Lens
  /** The file's own kind: a test file's parts are tests, a document's sections documentation. */
  fileKind: FileKind
  /** The file's system colour, under the systems lens. */
  systemColor: string | null
  git: boolean
  /** Per part: commits in the range that changed it. */
  activity: Int32Array | null
  /** Per part: last change up to the range's end, and anywhere in the loaded history. */
  times: Float64Array | null
  ever: Float64Array | null
  asOf: number
  olderThanLoaded: boolean
  /** Per part: 0 unchanged, 1 changed, 2 every line new since the last commit. */
  change: Uint8Array | null
  hover: Part | null
  selected: Part | null
  /** Lines of the file agents point at. */
  pointed: readonly (readonly [number, number])[]
  pulse: number
  labels: boolean
}

const DATA = new Set(['variable', 'constant', 'field', 'property', 'signal', 'uniform', 'varying', 'enum', 'type', 'event', 'delegate'])

/** What a part is, in the map's kinds: declarations are code, data declarations data, tests tests. */
export function partKind(part: Part, fileKind: FileKind): FileKind {
  if (fileKind === 'docs' || fileKind === 'test') return fileKind
  if (part.kind === 'test') return 'test'
  if (part.kind === 'top') return 'docs'
  if (DATA.has(part.kind) || isBlock(part)) return 'data'
  return 'code'
}

export function fillForPart(scene: PartsScene, part: Part): string | CanvasPattern {
  const { palette } = scene
  const kind = partKind(part, scene.fileKind)
  if (scene.lens === 'kind') return palette.kind[kind]
  if (scene.lens === 'systems') return scene.systemColor ?? palette.ground[kind]
  if (!scene.git) return palette.unsampled
  let step: number
  if (scene.lens === 'age') {
    if (!scene.times || !scene.ever) return palette.unsampled
    step = partAgeStep(scene.times[part.index], scene.ever[part.index], scene.asOf, scene.olderThanLoaded, scene.change?.[part.index] ?? 0)
    if (step === UNKNOWN_AGE) return palette.unsampled
  } else {
    if (!scene.activity) return palette.unsampled
    step = activityStep(scene.activity[part.index])
  }
  return step > 0 ? palette.heat[step] : palette.ground[kind]
}

const tagOf = (part: Part): PriceTag => ({ lines: true, text: format.count(part.lines, true) })

export function drawParts(ctx: CanvasRenderingContext2D, scene: PartsScene, width: number, height: number, dpr: number, view: View) {
  const { palette } = scene
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.globalAlpha = 1
  ctx.fillStyle = palette.gutter
  ctx.fillRect(0, 0, width, height)
  ctx.setTransform(dpr * view.sx, 0, 0, dpr * view.sy, dpr * view.tx, dpr * view.ty)

  const minW = 0.3 / view.sx
  const minH = 0.3 / view.sy
  for (const it of scene.items) {
    if (it.w < minW || it.h < minH) continue
    if (it.open) {
      if (it.depth > 0) {
        ctx.fillStyle = it.depth === 1 ? palette.region : palette.folder
        ctx.fillRect(it.x, it.y, it.w, it.h)
      }
      continue
    }
    ctx.fillStyle = fillForPart(scene, it.node)
    ctx.fillRect(it.x, it.y, it.w, it.h)
  }

  if (scene.labels) {
    ctx.textBaseline = 'middle'
    for (const it of scene.items) {
      if (it.open) {
        if (it.header) header(ctx, scene, it)
      } else if (it.w >= 40 && it.h >= 16) {
        leaf(ctx, scene, it)
      }
    }
  }
  overlays(ctx, scene, view)
}

function header(ctx: CanvasRenderingContext2D, scene: PartsScene, it: Item<Part>) {
  const region = it.depth === 1
  const size = region ? 12.5 : 11
  const x = it.x + (region ? 6 : 5)
  const y = it.y + it.header / 2 + 0.5
  const room = it.w - 10
  const tag = tagOf(it.node)
  const tagSpace = tagWidth(ctx, tag, 11)
  ctx.font = `600 ${size}px ${FONT}`
  const showTag = room > tagSpace + 40
  const shown = fit(ctx, it.node.name, showTag ? room - tagSpace - 7 : room)
  ctx.fillStyle = scene.palette.ink
  ctx.fillText(shown, x, y)
  if (showTag) {
    const nameWidth = ctx.measureText(shown).width
    ctx.fillStyle = scene.palette.ink3
    drawTag(ctx, tag, x + nameWidth + 7, y, 11)
  }
}

function leaf(ctx: CanvasRenderingContext2D, scene: PartsScene, it: Item<Part>) {
  const fill = fillForPart(scene, it.node)
  ctx.fillStyle = typeof fill === 'string' ? (prefersDarkText(fill) ? DARK_TEXT : LIGHT_TEXT) : scene.palette.ink2
  const x = it.x + 4
  const room = it.w - 8 - (scene.change?.[it.node.index] === 2 ? CORNER : 0)
  ctx.font = `11px ${FONT}`
  ctx.fillText(fit(ctx, it.node.name, room), x, it.y + 9)
  if (it.h >= 30) {
    const tag = tagOf(it.node)
    if (tagWidth(ctx, tag, 10) <= room) {
      ctx.globalAlpha = 0.75
      drawTag(ctx, tag, x, it.y + 22, 10)
      ctx.globalAlpha = 1
    }
  }
}

/** Does a part hold any of these lines? */
const holds = (part: Part, lines: readonly (readonly [number, number])[]) => lines.some(([from, to]) => from <= part.to && to >= part.from)

function overlays(ctx: CanvasRenderingContext2D, scene: PartsScene, view: View) {
  const outline = (item: Item<Part>, width: number, color: string, inset = 0) => {
    const ix = inset / view.sx
    const iy = inset / view.sy
    const wx = width / view.sx
    const wy = width / view.sy
    if (item.w <= 2 * ix + wx || item.h <= 2 * iy + wy) return
    ctx.lineWidth = Math.min(wx, wy)
    ctx.strokeStyle = color
    ctx.strokeRect(item.x + ix + wx / 2, item.y + iy + wy / 2, item.w - 2 * ix - wx, item.h - 2 * iy - wy)
  }
  const corner = (item: Item<Part>, color: string) => {
    const s = Math.min(CORNER, (item.w * view.sx) / 3, (item.h * view.sy) / 3)
    if (s < 5) return
    const [sx, sy] = [s / view.sx, s / view.sy]
    ctx.fillStyle = color
    ctx.beginPath()
    ctx.moveTo(item.x + item.w - sx, item.y)
    ctx.lineTo(item.x + item.w, item.y)
    ctx.lineTo(item.x + item.w, item.y + sy)
    ctx.closePath()
    ctx.fill()
  }
  // Marks go on the deepest drawn part: an open part only shows what none of the parts drawn inside it shows.
  const within = (outer: Part, inner: Part) => {
    for (let p = inner.parent; p; p = p.parent) if (p === outer) return true
    return false
  }
  const drawnBelow = (it: Item<Part>, test: (p: Part) => boolean) =>
    it.open && scene.items.some(other => !other.open && within(it.node, other.node) && test(other.node))

  if (scene.change) {
    const changed = (p: Part) => (scene.change?.[p.index] ?? 0) > 0
    for (const it of scene.items) {
      if (it.depth === 0 || !changed(it.node) || drawnBelow(it, changed)) continue
      outline(it, 2, scene.palette.change)
      if (scene.change[it.node.index] === 2 && !it.open) corner(it, scene.palette.change)
    }
  }
  if (scene.pointed.length > 0) {
    const pointed = (p: Part) => holds(p, scene.pointed)
    const width = 3 + 3 * scene.pulse
    for (const it of scene.items) {
      if (it.depth === 0 || !pointed(it.node) || drawnBelow(it, pointed)) continue
      outline(it, width, scene.palette.pointer)
      outline(it, 1, scene.palette.surface, width)
    }
  }
  for (const [part, width] of [[scene.hover, 1], [scene.selected, 2]] as const) {
    const it = part ? scene.items.find(i => i.node === part) : undefined
    if (it && it.depth > 0) outline(it, width, scene.palette.ink)
  }
}
