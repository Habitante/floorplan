import { effect } from '@preact/signals'
import { batch } from '@preact/signals'
import { pointedLines, pointedNodes, pulse } from './board'
import { send } from './bridge'
import { arrivedPaths, changeCounts, changeWords, describeChange } from './changes'
import * as format from './format'
import { GLYPHS, iconFontsLoaded, type IconName } from './glyphs'
import { hitTest, layoutTree, type Item } from './layout'
import { contains, displayName, LINES_AREA, weightOf, type Footprint, type Model, type Node } from './model'
import { readPalette, type Palette } from './palette'
import { systemCodes, systemOfFile, systemSlot } from './meaning'
import {
  findPart, largestParts, lineHistoryOf, loadOutline, outlineOf, partActivity, partHistory, partsLoaded, partTimes, treeOf,
  type Part, type PartHistory, type PartTree,
} from './parts'
import { drawParts } from './render-parts'
import { relatedNodes, relations } from './relations'
import { drawScene, IDENTITY, type View } from './render'
import type { Rect } from './squarify'
import {
  activity, age, description, focus, footprint, hovered, inside, isolated, lens, model, openReader, pointers, range, reader, readerFocus, selected,
  selectedCommit, selectedPart, systemMap, touched, zoomInto, zoomTo,
} from './state'

const ANIMATION_MS = 260
/** How long a new file's outline pulses after a refresh brings it onto the map. */
const ARRIVAL_MS = 2000

interface Animation {
  /** 'in': the new layout grows out of `from`; 'out': the new layout shrinks so `from` fills the view. */
  mode: 'in' | 'out'
  from: Rect
  start: number
}

/** Owns the map canvas: layout cache, drawing, zoom animation and pointer interaction. */
export class MapController {
  private readonly ctx: CanvasRenderingContext2D
  private palette: Palette
  private items: Item[] = []
  private itemByNode = new Map<Node, Item>()
  private laidOut: { model: Model; focus: Node; footprint: Footprint; tree: PartTree | null; width: number; height: number } | null = null
  /** Zoomed into a file: its parts, laid out over the whole map. */
  private parts: { file: Node; tree: PartTree; items: Item<Part>[] } | null = null
  private hoveredPart: Part | null = null
  private measured: { tree: PartTree; history: unknown; model: Model; value: PartHistory } | null = null
  /** The node the tooltip is for and where, to fill in parts that arrive later. */
  private tipAt: { node: Node; x: number; y: number } | null = null
  private width = 0
  private height = 0
  private dpr = 1
  private frame = 0
  private animation: Animation | null = null
  private reportedModel: Model | null = null
  private lastWheel = 0
  private readonly cleanup: (() => void)[] = []
  private readonly reducedMotion = matchMedia('(prefers-reduced-motion: reduce)')
  /** New files that refreshes brought onto the map, by path, with when they arrived. */
  private readonly arrivals = new Map<string, number>()

  constructor(
    private readonly host: HTMLElement,
    private readonly canvas: HTMLCanvasElement,
    private readonly tip: HTMLElement,
  ) {
    this.ctx = canvas.getContext('2d')!
    this.palette = readPalette(this.ctx)

    const resize = new ResizeObserver(() => this.resize())
    resize.observe(host)
    this.cleanup.push(() => resize.disconnect())

    const scheme = matchMedia('(prefers-color-scheme: dark)')
    const onScheme = () => {
      this.palette = readPalette(this.ctx)
      this.request()
    }
    scheme.addEventListener('change', onScheme)
    this.cleanup.push(() => scheme.removeEventListener('change', onScheme))

    this.cleanup.push(effect(() => {
      void inside.value, partsLoaded.value
      this.relayout(model.value, focus.value, footprint.value)
    }))
    // A tooltip shows a file's largest parts once its outline arrives.
    this.cleanup.push(effect(() => {
      void partsLoaded.value
      const at = this.tipAt
      if (at && !this.tip.hidden && !this.parts) this.showTip(at.node, { clientX: at.x, clientY: at.y } as MouseEvent)
    }))
    // A new file pulses once when a refresh of the same project brings it, unless motion is reduced.
    let shown: Model | null = null
    this.cleanup.push(effect(() => {
      const m = model.value
      if (m?.scan.project.root !== shown?.scan.project.root) this.arrivals.clear()
      else if (m && shown && m !== shown && !this.reducedMotion.matches) {
        const now = performance.now()
        for (const path of arrivedPaths(shown, m)) this.arrivals.set(path, now)
      }
      shown = m
    }))
    this.cleanup.push(effect(() => {
      void lens.value, activity.value, hovered.value, selected.value, touched.value, pointers.value, relations.value, iconFontsLoaded.value
      void selectedPart.value, range.value
      void isolated.value, lens.value === 'systems' && systemMap.value
      if (lens.value === 'age') void age.value
      this.request()
    }))

    this.listen(canvas, 'pointermove', e => this.onMove(e as PointerEvent))
    this.listen(canvas, 'pointerleave', () => this.clearHover())
    this.listen(canvas, 'click', e => this.onClick(e as MouseEvent))
    this.listen(canvas, 'dblclick', e => this.onDoubleClick(e as MouseEvent))
    this.listen(canvas, 'wheel', e => this.onWheel(e as WheelEvent), { passive: false })
    this.listen(canvas, 'mouseup', e => {
      if ((e as MouseEvent).button === 3) zoomOut()
    })
    this.resize()
  }

  dispose() {
    cancelAnimationFrame(this.frame)
    for (const fn of this.cleanup) fn()
  }

  private listen(target: EventTarget, type: string, handler: (e: Event) => void, options?: AddEventListenerOptions) {
    target.addEventListener(type, handler, options)
    this.cleanup.push(() => target.removeEventListener(type, handler, options))
  }

  private resize() {
    const rect = this.host.getBoundingClientRect()
    const width = Math.max(0, Math.floor(rect.width))
    const height = Math.max(0, Math.floor(rect.height))
    const dpr = window.devicePixelRatio || 1
    if (width === this.width && height === this.height && dpr === this.dpr) return
    this.hideTip()
    this.width = width
    this.height = height
    this.dpr = dpr
    // Resizing the bitmap clears it, so always redraw.
    this.canvas.width = Math.round(width * dpr)
    this.canvas.height = Math.round(height * dpr)
    this.relayout(model.peek(), focus.peek(), footprint.peek())
    this.request()
  }

  private relayout(m: Model | null, f: Node | null, fp: Footprint) {
    if (!m || !f || this.width === 0 || this.height === 0) {
      this.items = []
      this.itemByNode = new Map()
      this.laidOut = null
      this.request()
      return
    }
    // Zoomed into a file, its parts are shown once its outline is in; one without parts is left.
    const file = inside.peek()
    const outline = file ? outlineOf(file) : null
    const tree = file && outline && outline.symbols.length > 0 ? treeOf(file, outline) : null
    if (file && (outline === null || outline?.symbols.length === 0)) queueMicrotask(() => { if (inside.peek() === file) inside.value = null })

    const before = this.laidOut
    if (before && before.model === m && before.focus === f && before.footprint === fp && before.tree === tree &&
        before.width === this.width && before.height === this.height) return

    const bounds = { x: 0, y: 0, w: this.width, h: this.height }
    const previousItems = this.items
    const previousParts = this.parts
    this.items = layoutTree(f, bounds, weightOf(fp))
    this.itemByNode = new Map(this.items.map(i => [i.node, i]))
    this.parts = file && tree ? { file, tree, items: layoutTree(tree.root, bounds, p => p.lines) } : null
    if (!this.parts) this.hoveredPart = null
    this.laidOut = { model: m, focus: f, footprint: fp, tree, width: this.width, height: this.height }

    this.animation = null
    const now = performance.now()
    if (before && before.model === m && !this.reducedMotion.matches && !previousParts && this.parts) {
      // Into a file: its parts grow out of its rectangle.
      const from = previousItems.find(i => i.node === this.parts!.file)
      if (from) this.animation = { mode: 'in', from, start: now }
    } else if (before && before.model === m && !this.reducedMotion.matches && previousParts && !this.parts) {
      // Out of a file: the folder shrinks back to it.
      const from = this.itemByNode.get(previousParts.file)
      if (from) this.animation = { mode: 'out', from, start: now }
    } else if (before && before.model === m && before.footprint === fp && before.focus !== f && !this.parts && !this.reducedMotion.matches) {
      if (contains(before.focus, f)) {
        const from = nearest(new Map(previousItems.map(i => [i.node, i])), f)
        if (from) this.animation = { mode: 'in', from, start: performance.now() }
      } else if (contains(f, before.focus)) {
        const from = nearest(this.itemByNode, before.focus)
        if (from) this.animation = { mode: 'out', from, start: performance.now() }
      }
    }
    this.hideTip()
    this.request()
  }

  private request() {
    if (!this.frame) this.frame = requestAnimationFrame(() => this.draw())
  }

  private draw() {
    this.frame = 0
    const m = model.peek()
    if (!m || this.items.length === 0) {
      this.ctx.setTransform(1, 0, 0, 1, 0, 0)
      this.ctx.fillStyle = this.palette.surface
      this.ctx.fillRect(0, 0, this.canvas.width, this.canvas.height)
      return
    }

    let view = IDENTITY
    const a = this.animation
    if (a) {
      const t = Math.min(1, (performance.now() - a.start) / ANIMATION_MS)
      const e = 1 - (1 - t) ** 3
      const full = { x: 0, y: 0, w: this.width, h: this.height }
      view = a.mode === 'in' ? mapRect(full, lerp(a.from, full, e)) : mapRect(lerp(a.from, full, e), full)
      if (t >= 1) this.animation = null
    }

    const now = performance.now()
    const pointing = pointers.peek()
    const emphasis = this.reducedMotion.matches ? 0 : pulse(pointing, now)
    const arrived = new Map<Node, number>()
    for (const [path, at] of this.arrivals) {
      const t = (now - at) / ARRIVAL_MS
      const node = t < 1 ? m.byPath.get(path) : undefined
      if (node) arrived.set(node, (1 - t) ** 2)
      else this.arrivals.delete(path)
    }
    if (this.parts) {
      this.drawParts(m, view, emphasis)
      if (this.animation || emphasis > 0) this.request()
      return
    }
    drawScene(this.ctx, {
      model: m,
      items: this.items,
      itemByNode: this.itemByNode,
      palette: this.palette,
      lens: lens.peek(),
      footprint: footprint.peek(),
      activity: activity.peek(),
      age: lens.peek() === 'age' ? age.peek() : null,
      hover: hovered.peek(),
      selected: selected.peek(),
      touched: touched.peek(),
      systems: lens.peek() === 'systems' ? systemMap.peek() : null,
      codes: lens.peek() === 'systems' ? systemCodes(description.peek()) : null,
      isolated: isolated.peek(),
      pointed: pointedNodes(m, pointing),
      pulse: emphasis,
      arrivals: arrived,
      related: relatedNodes(m, relations.peek()),
      labels: this.animation === null,
    }, this.width, this.height, this.dpr, view)

    if (this.animation || emphasis > 0 || arrived.size > 0) this.request()
    if (this.reportedModel !== m) {
      this.reportedModel = m
      send({ type: 'rendered', files: m.root.files, items: this.items.length, ms: Math.round(performance.now()) })
    }
  }

  private drawParts(m: Model, view: View, emphasis: number) {
    const { file, tree, items } = this.parts!
    const history = this.historyOf(m, file, tree)
    const [from, to] = range.peek()
    const fill = lens.peek()
    const ages = fill === 'age' ? age.peek() : null
    const times = history && ages ? partTimes(history, m, to) : null
    const system = fill === 'systems' ? systemMap.peek()?.[file.index] ?? -1 : -1
    const choice = selectedPart.peek()
    drawParts(this.ctx, {
      items,
      palette: this.palette,
      lens: fill,
      fileKind: file.kind ?? 'code',
      systemColor: system >= 0 ? this.palette.systems[systemSlot(system)] : null,
      git: m.scan.project.git,
      activity: history ? partActivity(history, from, to) : null,
      times: times?.times ?? null,
      ever: times?.ever ?? null,
      asOf: ages?.asOf ?? 0,
      olderThanLoaded: ages?.olderThanLoaded ?? false,
      change: history?.change ?? null,
      hover: this.hoveredPart,
      selected: choice?.path === file.path ? findPart(tree, choice) : null,
      pointed: pointedLines(pointers.peek(), file.path),
      pulse: emphasis,
      labels: this.animation === null,
    }, this.width, this.height, this.dpr, view)
  }

  /** The parts' commits and uncommitted changes, once the file's line history is in. */
  private historyOf(m: Model, file: Node, tree: PartTree): PartHistory | null {
    const change = file.src.change
    const fresh = change === 'untracked' || change === 'added' || change === 'renamed'
    const lines = lineHistoryOf(m, file)
    if (lines === undefined && !fresh) return null
    const known = this.measured
    if (known && known.tree === tree && known.history === lines && known.model === m) return known.value
    const value = partHistory(tree, lines ?? null, m, fresh)
    this.measured = { tree, history: lines, model: m, value }
    return value
  }

  private partAt(e: MouseEvent): Part | null {
    if (this.animation || !this.parts) return null
    const r = this.canvas.getBoundingClientRect()
    return hitTest(this.parts.items, e.clientX - r.left, e.clientY - r.top)?.node ?? null
  }

  /** Zooms into a file when it has parts to show. */
  private enter(file: Node) {
    loadOutline(file).then(outline => {
      if (outline && outline.symbols.length > 0 && model.peek()?.byPath.get(file.path) === file) zoomInto(file)
    })
  }

  private nodeAt(e: MouseEvent): Node | null {
    if (this.animation || this.parts) return null
    const r = this.canvas.getBoundingClientRect()
    return hitTest(this.items, e.clientX - r.left, e.clientY - r.top)?.node ?? null
  }

  private onMove(e: PointerEvent) {
    if (this.parts) {
      const part = this.partAt(e)
      if (part !== this.hoveredPart) {
        this.hoveredPart = part
        this.request()
      }
      if (part && part !== this.parts.tree.root) this.showPartTip(this.parts.file, part, e)
      else this.hideTip()
      return
    }
    const node = this.nodeAt(e)
    if (hovered.peek() !== node) hovered.value = node
    if (node) this.showTip(node, e)
    else this.hideTip()
  }

  private clearHover() {
    hovered.value = null
    if (this.hoveredPart) {
      this.hoveredPart = null
      this.request()
    }
    this.hideTip()
  }

  private onClick(e: MouseEvent) {
    if (this.parts) {
      // A part is read where it is in its file.
      const part = this.partAt(e)
      const file = this.parts.file
      if (!part || part === this.parts.tree.root) return
      batch(() => {
        selectedPart.value = { path: file.path, id: part.id, from: part.from, to: part.to }
        readerFocus.value = { path: file.path, from: part.from, to: part.to }
        openReader(file)
      })
      return
    }
    const node = this.nodeAt(e)
    if (!node) return
    if (node.isFile) {
      // Read the file; within a selected commit (or the uncommitted changes), read its diff.
      const commit = selectedCommit.peek()
      const marks = touched.peek()
      openReader(node, commit !== null && marks?.[node.index] ? commit : null)
      return
    }
    selectedCommit.value = null
    reader.value = null
    selected.value = node === focus.peek() ? null : node
  }

  private onDoubleClick(e: MouseEvent) {
    const node = this.nodeAt(e)
    if (!node) return
    if (node.isFile) this.enter(node)
    else if (node !== focus.peek()) zoomTo(node)
  }

  private onWheel(e: WheelEvent) {
    e.preventDefault()
    const now = performance.now()
    if (now - this.lastWheel < 180 || e.deltaY === 0) return
    this.lastWheel = now
    if (e.deltaY > 0) {
      zoomOut()
      return
    }
    const under = this.nodeAt(e)
    const f = focus.peek()
    if (!under || !f) return
    // A file in the folder on view opens into its parts.
    if (under.isFile && under.parent === f) {
      this.enter(under)
      return
    }
    // Step one level towards the folder under the pointer.
    let step: Node | null = under.isFile ? under.parent : under
    while (step && step.parent !== f) step = step.parent
    if (step && !step.isFile) zoomTo(step)
  }

  private showTip(node: Node, e: MouseEvent) {
    const m = model.peek()
    if (!m) return
    this.tipAt = { node, x: e.clientX, y: e.clientY }
    const counts = activity.peek()
    const parts: (string | [IconName, string])[] = []
    if (!node.isFile) parts.push(['file', format.int(node.files)])
    const src = node.src
    if (node.lines > 0 || (src.kind && LINES_AREA.has(src.kind))) parts.push(['lines', format.int(node.lines)])
    if (src.lines !== undefined && src.kind === 'data') parts.push(['lines', format.int(src.lines)])
    if (src.bytes !== undefined) parts.push(format.bytes(node.bytes))
    if (m.scan.project.git && counts) parts.push(['commit', format.int(counts[node.index])])
    const last = lens.peek() === 'age' ? age.peek()?.times[node.index] ?? 0 : 0
    if (last > 0) parts.push(['clock', format.ago(format.fromUnix(last))])
    if (node.isFile && src.kind) parts.push(src.kind)

    const path = node === m.root ? displayName(m, node) : node.path + (node.isFile ? '' : '/')
    const price = document.createElement('div')
    price.className = 'tip-price num'
    parts.forEach((part, i) => {
      if (i > 0) price.append(' · ')
      if (typeof part === 'string') {
        price.append(part)
      } else {
        const icon = document.createElement('span')
        icon.className = 'icon'
        icon.textContent = GLYPHS[part[0]]
        price.append(icon, part[1])
      }
    })
    const title = document.createElement('div')
    title.className = 'tip-path'
    title.textContent = path
    this.tip.replaceChildren(title, price)
    const inside = node.isFile ? '' : changeWords(changeCounts(m, node))
    const change = node.isFile ? describeChange(node) : inside ? `${inside}, uncommitted` : null
    const report = description.peek()
    const entry = systemOfFile(report, node)
    const system = entry && report?.description ? `${entry.name} (${systemCodes(report)[report.description.systems.indexOf(entry)]})` : null
    for (const [text, className] of [[change, 'tip-change'], [system, 'tip-note'], [src.note, 'tip-note']] as const) {
      if (!text) continue
      const line = document.createElement('div')
      line.className = className
      line.textContent = text
      this.tip.append(line)
    }
    // A file's largest parts, once its outline is in.
    const outline = node.isFile ? outlineOf(node) : null
    const largest = outline ? largestParts(treeOf(node, outline)) : []
    if (largest.length > 0) this.tip.append(partsLine(largest))
    this.place(e)
  }

  private showPartTip(file: Node, part: Part, e: MouseEvent) {
    const m = model.peek()
    if (!m) return
    this.tipAt = null
    const parts: (string | [IconName, string])[] = [['lines', format.int(part.lines)], part.from === part.to ? `line ${part.from}` : `lines ${part.from}–${part.to}`]
    const history = this.parts && this.historyOf(m, file, this.parts.tree)
    const [from, to] = range.peek()
    if (m.scan.project.git && history) parts.push(['commit', format.int(partActivity(history, from, to)[part.index])])
    const last = history && lens.peek() === 'age' ? partTimes(history, m, to).times[part.index] : 0
    if (last > 0) parts.push(['clock', format.ago(format.fromUnix(last))])
    parts.push(part.kind === 'top' ? 'comments and imports' : part.kind)
    const title = document.createElement('div')
    title.className = 'tip-path'
    title.textContent = `${file.name} › ${part.id ?? part.name}`
    this.tip.replaceChildren(title, priceLine(parts))
    const change = history?.change[part.index] ?? 0
    if (change > 0) {
      const line = document.createElement('div')
      line.className = 'tip-change'
      line.textContent = change === 2 ? 'New, uncommitted' : 'Changed, uncommitted'
      this.tip.append(line)
    }
    if (this.parts && !part.isFile) this.tip.append(partsLine(largestParts({ root: part, parts: this.parts.tree.parts.filter(p => within(part, p)) })))
    this.place(e)
  }

  private place(e: MouseEvent) {
    this.tip.hidden = false
    const host = this.host.getBoundingClientRect()
    const tw = this.tip.offsetWidth
    const th = this.tip.offsetHeight
    let x = e.clientX - host.left + 14
    let y = e.clientY - host.top + 16
    if (x + tw > host.width - 4) x = Math.max(4, e.clientX - host.left - tw - 14)
    if (y + th > host.height - 4) y = Math.max(4, e.clientY - host.top - th - 12)
    this.tip.style.transform = `translate(${x}px, ${y}px)`
  }

  private hideTip() {
    this.tip.hidden = true
    this.tipAt = null
  }
}

/** Is a part inside another? */
function within(outer: Part, inner: Part): boolean {
  for (let p = inner.parent; p; p = p.parent) if (p === outer) return true
  return false
}

/** The measurements line of a tooltip: words and glyphs with numbers, between dots. */
function priceLine(parts: (string | [IconName, string])[]): HTMLElement {
  const price = document.createElement('div')
  price.className = 'tip-price num'
  parts.forEach((part, i) => {
    if (i > 0) price.append(' · ')
    if (typeof part === 'string') {
      price.append(part)
    } else {
      const icon = document.createElement('span')
      icon.className = 'icon'
      icon.textContent = GLYPHS[part[0]]
      price.append(icon, part[1])
    }
  })
  return price
}

/** A file's or a part's largest parts, each with its lines. */
function partsLine(parts: readonly Part[]): HTMLElement {
  const line = document.createElement('div')
  line.className = 'tip-parts num'
  parts.forEach((part, i) => {
    if (i > 0) line.append(' · ')
    const icon = document.createElement('span')
    icon.className = 'icon'
    icon.textContent = GLYPHS.lines
    line.append(`${part.name} `, icon, format.int(part.lines))
  })
  return line
}

export function zoomOut() {
  // Out of a file first, back to its folder with the file selected.
  const file = inside.peek()
  if (file) {
    batch(() => {
      inside.value = null
      selectedPart.value = null
      selected.value = file
    })
    return
  }
  const f = focus.peek()
  if (f?.parent) {
    selected.value = f
    zoomTo(f.parent)
  }
}

function nearest(byNode: ReadonlyMap<Node, Item>, node: Node): Rect | undefined {
  for (let n: Node | null = node; n; n = n.parent) {
    const item = byNode.get(n)
    if (item) return item
  }
  return undefined
}

function lerp(a: Rect, b: Rect, t: number): Rect {
  return { x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t, w: a.w + (b.w - a.w) * t, h: a.h + (b.h - a.h) * t }
}

/** The view that maps rectangle `a` (layout space) onto rectangle `b` (screen). */
function mapRect(a: Rect, b: Rect): View {
  const sx = b.w / Math.max(a.w, 1e-6)
  const sy = b.h / Math.max(a.h, 1e-6)
  return { sx, sy, tx: b.x - a.x * sx, ty: b.y - a.y * sy }
}
