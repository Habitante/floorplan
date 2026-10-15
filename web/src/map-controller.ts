import { effect } from '@preact/signals'
import { pointedNodes, pulse } from './board'
import { send } from './bridge'
import * as format from './format'
import { GLYPHS, iconFontsLoaded, type IconName } from './glyphs'
import { hitTest, layoutTree, type Item } from './layout'
import { contains, displayName, weightOf, type Footprint, type Model, type Node } from './model'
import { readPalette, type Palette } from './palette'
import { drawScene, IDENTITY, type View } from './render'
import type { Rect } from './squarify'
import {
  activity, focus, footprint, hovered, lens, model, openReader, pointers, reader, selected, selectedCommit, touched, zoomTo,
} from './state'

const ANIMATION_MS = 260

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
  private laidOut: { model: Model; focus: Node; footprint: Footprint; width: number; height: number } | null = null
  private width = 0
  private height = 0
  private dpr = 1
  private frame = 0
  private animation: Animation | null = null
  private reportedModel: Model | null = null
  private lastWheel = 0
  private readonly cleanup: (() => void)[] = []
  private readonly reducedMotion = matchMedia('(prefers-reduced-motion: reduce)')

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

    this.cleanup.push(effect(() => this.relayout(model.value, focus.value, footprint.value)))
    this.cleanup.push(effect(() => {
      void lens.value, activity.value, hovered.value, selected.value, touched.value, pointers.value, iconFontsLoaded.value
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
    const before = this.laidOut
    if (before && before.model === m && before.focus === f && before.footprint === fp &&
        before.width === this.width && before.height === this.height) return

    const previousItems = this.items
    this.items = layoutTree(f, { x: 0, y: 0, w: this.width, h: this.height }, weightOf(fp))
    this.itemByNode = new Map(this.items.map(i => [i.node, i]))
    this.laidOut = { model: m, focus: f, footprint: fp, width: this.width, height: this.height }

    this.animation = null
    if (before && before.model === m && before.footprint === fp && before.focus !== f && !this.reducedMotion.matches) {
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

    const pointing = pointers.peek()
    const emphasis = this.reducedMotion.matches ? 0 : pulse(pointing, performance.now())
    drawScene(this.ctx, {
      model: m,
      items: this.items,
      itemByNode: this.itemByNode,
      palette: this.palette,
      lens: lens.peek(),
      footprint: footprint.peek(),
      activity: activity.peek(),
      hover: hovered.peek(),
      selected: selected.peek(),
      touched: touched.peek(),
      pointed: pointedNodes(m, pointing),
      pulse: emphasis,
      labels: this.animation === null,
    }, this.width, this.height, this.dpr, view)

    if (this.animation || emphasis > 0) this.request()
    if (this.reportedModel !== m) {
      this.reportedModel = m
      send({ type: 'rendered', files: m.root.files, items: this.items.length, ms: Math.round(performance.now()) })
    }
  }

  private nodeAt(e: MouseEvent): Node | null {
    if (this.animation) return null
    const r = this.canvas.getBoundingClientRect()
    return hitTest(this.items, e.clientX - r.left, e.clientY - r.top)?.node ?? null
  }

  private onMove(e: PointerEvent) {
    const node = this.nodeAt(e)
    if (hovered.peek() !== node) hovered.value = node
    if (node) this.showTip(node, e)
    else this.hideTip()
  }

  private clearHover() {
    hovered.value = null
    this.hideTip()
  }

  private onClick(e: MouseEvent) {
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
    if (!node.isFile && node !== focus.peek()) zoomTo(node)
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
    // Step one level towards the folder under the pointer.
    let step: Node | null = under.isFile ? under.parent : under
    while (step && step.parent !== f) step = step.parent
    if (step && !step.isFile) zoomTo(step)
  }

  private showTip(node: Node, e: MouseEvent) {
    const m = model.peek()
    if (!m) return
    const counts = activity.peek()
    const parts: (string | [IconName, string])[] = []
    if (!node.isFile) parts.push(['file', format.int(node.files)])
    const src = node.src
    if (node.lines > 0 || src.kind === 'code' || src.kind === 'docs') parts.push(['lines', format.int(node.lines)])
    if (src.lines !== undefined && src.kind === 'data') parts.push(['lines', format.int(src.lines)])
    if (src.bytes !== undefined) parts.push(format.bytes(node.bytes))
    if (m.scan.project.git && counts) parts.push(['commit', format.int(counts[node.index])])
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
    if (src.note) {
      const note = document.createElement('div')
      note.className = 'tip-note'
      note.textContent = src.note
      this.tip.append(note)
    }

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
  }
}

export function zoomOut() {
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
