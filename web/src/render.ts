import { activityStep, nodeAgeStep, UNKNOWN_AGE, type Ages } from './activity'
import { changeCounts, changeKind, changeSummary } from './changes'
import * as format from './format'
import { GLYPHS, ICON_FONT } from './glyphs'
import type { Item } from './layout'
import { dominantKind, displayName, LINES_AREA, type Footprint, type Model, type Node } from './model'
import { prefersDarkText, type Palette } from './palette'
import type { Lens } from './state'

/** Maps layout coordinates to the screen: screen = layout * s + t. */
export interface View {
  sx: number
  sy: number
  tx: number
  ty: number
}

export const IDENTITY: View = { sx: 1, sy: 1, tx: 0, ty: 0 }

export interface Scene {
  model: Model
  items: readonly Item[]
  itemByNode: ReadonlyMap<Node, Item>
  palette: Palette
  lens: Lens
  footprint: Footprint
  activity: Int32Array | null
  /** Last commit times and the moment they are measured from, for the age lens. */
  age: Ages | null
  hover: Node | null
  selected: Node | null
  touched: Uint8Array | null
  /** Files and folders agents are pointing at. */
  pointed: readonly Node[]
  /** Extra emphasis for a pointer that just arrived, 0 to 1. */
  pulse: number
  /** New files that just arrived on the map, with their emphasis, 1 on arrival fading to 0. */
  arrivals: ReadonlyMap<Node, number>
  /** Labels are skipped while animating. */
  labels: boolean
}

const FONT = '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif'
/** Edge of a new file's corner mark, in screen pixels. */
const CORNER = 14
const DARK_TEXT = '#0b0b0b'
const LIGHT_TEXT = '#ffffff'

/**
 * Kind fills with its full colour. Activity and age fill with the warm ramp, and leave what
 * they count as zero in the dim ground tint of its kind, so the map never turns uniformly grey.
 * An age the loaded history cannot tell is hatched.
 */
export function fillFor(scene: Scene, node: Node): string | CanvasPattern {
  const { palette } = scene
  const kind = dominantKind(scene.model, node, scene.footprint)
  if (scene.lens === 'kind') return palette.kind[kind]
  if (!scene.model.scan.project.git) return palette.unsampled
  let step: number
  if (scene.lens === 'age') {
    if (!scene.age) return palette.unsampled
    step = nodeAgeStep(scene.age, node)
    if (step === UNKNOWN_AGE) return palette.unsampled
  } else {
    if (!scene.activity) return palette.unsampled
    step = activityStep(scene.activity[node.index])
  }
  return step > 0 ? palette.heat[step] : palette.ground[kind]
}

/** A price tag: a line count (shown with the lines glyph) or a size with its unit. */
export interface PriceTag {
  lines: boolean
  text: string
}

/**
 * The node's price tag under the footprint. A file without a line weight shows the
 * measurement it does have (its own data lines, or its size) rather than a false zero.
 */
export function price(node: Node, footprint: Footprint, short: boolean): PriceTag {
  if (footprint === 'bytes') return { lines: false, text: format.bytes(node.bytes) }
  if (!node.isFile || (node.kind && LINES_AREA.has(node.kind))) return { lines: true, text: format.count(node.lines, short) }
  if (node.src.lines !== undefined) return { lines: true, text: format.count(node.src.lines, short) }
  return { lines: false, text: node.src.bytes !== undefined ? format.bytes(node.bytes) : '—' }
}

const GLYPH_GAP = 3

function tagWidth(ctx: CanvasRenderingContext2D, tag: PriceTag, size: number): number {
  ctx.font = `${size}px ${FONT}`
  let width = ctx.measureText(tag.text).width
  if (tag.lines) {
    ctx.font = `${size - 1}px ${ICON_FONT}`
    width += ctx.measureText(GLYPHS.lines).width + GLYPH_GAP
  }
  return width
}

function drawTag(ctx: CanvasRenderingContext2D, tag: PriceTag, x: number, y: number, size: number) {
  if (tag.lines) {
    ctx.font = `${size - 1}px ${ICON_FONT}`
    ctx.fillText(GLYPHS.lines, x, y)
    x += ctx.measureText(GLYPHS.lines).width + GLYPH_GAP
  }
  ctx.font = `${size}px ${FONT}`
  ctx.fillText(tag.text, x, y)
}

export function drawScene(ctx: CanvasRenderingContext2D, scene: Scene, width: number, height: number, dpr: number, view: View) {
  const { palette } = scene
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0)
  ctx.globalAlpha = 1
  ctx.fillStyle = palette.gutter
  ctx.fillRect(0, 0, width, height)
  ctx.setTransform(dpr * view.sx, 0, 0, dpr * view.sy, dpr * view.tx, dpr * view.ty)

  const minW = 0.3 / view.sx
  const minH = 0.3 / view.sy
  const touched = scene.touched

  for (const it of scene.items) {
    if (it.w < minW || it.h < minH) continue
    if (it.open) {
      if (it.depth > 0) {
        ctx.globalAlpha = 1
        ctx.fillStyle = it.depth === 1 ? palette.region : palette.folder
        ctx.fillRect(it.x, it.y, it.w, it.h)
      }
      continue
    }
    ctx.globalAlpha = touched && !touched[it.node.index] ? 0.22 : 1
    ctx.fillStyle = fillFor(scene, it.node)
    ctx.fillRect(it.x, it.y, it.w, it.h)
  }
  ctx.globalAlpha = 1

  if (scene.labels) drawLabels(ctx, scene)
  drawOverlays(ctx, scene, view)
}

function drawLabels(ctx: CanvasRenderingContext2D, scene: Scene) {
  ctx.textBaseline = 'middle'
  for (const it of scene.items) {
    const dim = scene.touched !== null && !scene.touched[it.node.index]
    ctx.globalAlpha = dim ? 0.45 : 1
    if (it.open) {
      if (it.header) headerLabel(ctx, scene, it)
    } else if (it.w >= 40 && it.h >= 16) {
      leafLabel(ctx, scene, it)
    }
  }
  ctx.globalAlpha = 1
}

function headerLabel(ctx: CanvasRenderingContext2D, scene: Scene, it: Item) {
  // Regions get a larger name than the folders inside them.
  const region = it.depth === 1
  const size = region ? 12.5 : 11
  const x = it.x + (region ? 6 : 5)
  const y = it.y + it.header / 2 + 0.5
  let room = it.w - 10
  const tag = price(it.node, scene.footprint, true)
  const tagSpace = tagWidth(ctx, tag, 11)

  // A region's uncommitted files by kind, at the right: "+2 ~1 −1". The name keeps its room first.
  const counts = region ? changeSummary(changeCounts(scene.model, it.node)) : ''
  ctx.font = `600 11px ${FONT}`
  const countsWidth = counts ? ctx.measureText(counts).width : 0
  if (counts && room > countsWidth + 50) {
    ctx.fillStyle = scene.palette.change
    ctx.fillText(counts, it.x + it.w - 6 - countsWidth, y)
    room -= countsWidth + 8
  }

  ctx.font = `600 ${size}px ${FONT}`
  const name = displayName(scene.model, it.node)
  const showTag = room > tagSpace + 40
  const shown = fit(ctx, name, showTag ? room - tagSpace - 7 : room)
  ctx.fillStyle = scene.palette.ink
  ctx.fillText(shown, x, y)
  if (showTag) {
    const nameWidth = ctx.measureText(shown).width
    ctx.fillStyle = scene.palette.ink3
    drawTag(ctx, tag, x + nameWidth + 7, y, 11)
  }
}

function leafLabel(ctx: CanvasRenderingContext2D, scene: Scene, it: Item) {
  const fill = fillFor(scene, it.node)
  ctx.fillStyle = typeof fill === 'string' ? (prefersDarkText(fill) ? DARK_TEXT : LIGHT_TEXT) : scene.palette.ink2
  const x = it.x + 4
  // A new file's corner mark takes the top right.
  const change = it.node.src.change
  const room = it.w - 8 - (change && changeKind(change) === 'new' ? CORNER : 0)
  ctx.font = `11px ${FONT}`
  ctx.fillText(fit(ctx, it.node.name, room), x, it.y + 9)
  if (it.h >= 30) {
    const tag = price(it.node, scene.footprint, true)
    if (tagWidth(ctx, tag, 10) <= room) {
      ctx.globalAlpha *= 0.75
      drawTag(ctx, tag, x, it.y + 22, 10)
    }
  }
}

function drawOverlays(ctx: CanvasRenderingContext2D, scene: Scene, view: View) {
  /** The item drawn for a node, or for its nearest drawn ancestor below the focus. */
  const drawn = (node: Node | null): Item | undefined => {
    let item: Item | undefined
    for (let n: Node | null = node; n && !item; n = n.parent) item = scene.itemByNode.get(n)
    return item && item.depth > 0 ? item : undefined
  }
  /** A stroke of `width` screen pixels, `inset` pixels inside the item's edge. */
  const ring = (item: Item | undefined, inset: number, width: number, color: string) => {
    if (!item) return
    const ix = inset / view.sx
    const iy = inset / view.sy
    const wx = width / view.sx
    const wy = width / view.sy
    if (inset > 0 && (item.w <= 2 * ix + wx || item.h <= 2 * iy + wy)) return
    ctx.lineWidth = Math.min(wx, wy)
    ctx.strokeStyle = color
    ctx.strokeRect(item.x + ix + wx / 2, item.y + iy + wy / 2, item.w - 2 * ix - wx, item.h - 2 * iy - wy)
  }
  const outline = (item: Item | undefined, width: number, color: string) => ring(item, 0, width, color)
  /** A filled triangle in the item's top right corner, up to `size` screen pixels along each edge. */
  const corner = (item: Item, size: number, color: string) => {
    const s = Math.min(size, (item.w * view.sx) / 3, (item.h * view.sy) / 3)
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

  if (scene.touched) {
    for (const it of scene.items) {
      if (!it.open && scene.touched[it.node.index] && it.w > 5 && it.h > 5) outline(it, 1, scene.palette.ink)
    }
  }
  // Uncommitted changes, always visible; files too small to draw mark their folder. New files
  // also get a filled corner, and a file that just arrived pulses once.
  const marked = new Map<Item, number>()
  for (const node of scene.model.changed) {
    const item = drawn(node)
    if (!item) continue
    marked.set(item, Math.max(marked.get(item) ?? 0, scene.arrivals.get(node) ?? 0))
    if (item.node === node && changeKind(node.src.change!) === 'new') corner(item, CORNER, scene.palette.change)
  }
  for (const [item, emphasis] of marked) outline(item, 2 + 3 * emphasis, scene.palette.change)
  // Agents' pointers, likewise. A surface hairline inside keeps the outline legible on any fill.
  const pointed = new Set<Item>()
  const width = 3 + 3 * scene.pulse
  for (const node of scene.pointed) {
    const item = drawn(node)
    if (item && !pointed.has(item)) {
      pointed.add(item)
      outline(item, width, scene.palette.pointer)
      ring(item, width, 1, scene.palette.surface)
    }
  }
  outline(drawn(scene.hover), 1, scene.palette.ink)
  outline(drawn(scene.selected), 2, scene.palette.ink)
}

/** Truncates text with an ellipsis to fit the width. */
function fit(ctx: CanvasRenderingContext2D, text: string, width: number): string {
  if (width <= 0) return ''
  if (ctx.measureText(text).width <= width) return text
  let lo = 0
  let hi = text.length
  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1
    if (ctx.measureText(text.slice(0, mid) + '…').width <= width) lo = mid
    else hi = mid - 1
  }
  return lo === 0 ? '' : text.slice(0, lo) + '…'
}
