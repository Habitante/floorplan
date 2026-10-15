import { effect } from '@preact/signals'
import * as format from './format'
import { readPalette, type Palette } from './palette'
import { DEFAULT_RANGE, model, range, selectedCommit } from './state'

/** Owns the history strip canvas: commits oldest to newest, drag to set the activity range. */
export class HistoryController {
  private readonly ctx: CanvasRenderingContext2D
  private palette: Palette
  private width = 0
  private height = 0
  private dpr = 1
  private frame = 0
  private hover: number | null = null
  private drag: { anchor: number; startX: number; moved: boolean } | null = null
  private readonly cleanup: (() => void)[] = []

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
      void model.value, range.value, selectedCommit.value
      this.request()
    }))

    const on = (type: string, fn: (e: PointerEvent) => void) => {
      const handler = (e: Event) => fn(e as PointerEvent)
      canvas.addEventListener(type, handler)
      this.cleanup.push(() => canvas.removeEventListener(type, handler))
    }
    on('pointerdown', e => this.onDown(e))
    on('pointermove', e => this.onMove(e))
    on('pointerup', e => this.onUp(e))
    on('pointerleave', () => this.setHover(null))
    on('dblclick', () => {
      const n = model.peek()?.commits.length ?? 0
      range.value = [Math.max(0, n - DEFAULT_RANGE), n]
    })
    this.resize()
  }

  dispose() {
    cancelAnimationFrame(this.frame)
    for (const fn of this.cleanup) fn()
  }

  private resize() {
    const r = this.host.getBoundingClientRect()
    const width = Math.floor(r.width)
    const height = Math.floor(r.height)
    const dpr = window.devicePixelRatio || 1
    if (width === this.width && height === this.height && dpr === this.dpr) return
    this.tip.hidden = true
    this.width = width
    this.height = height
    this.dpr = dpr
    this.canvas.width = Math.round(this.width * this.dpr)
    this.canvas.height = Math.round(this.height * this.dpr)
    this.request()
  }

  private request() {
    if (!this.frame) this.frame = requestAnimationFrame(() => this.draw())
  }

  /** Commits plus, when there are uncommitted changes, one pending slot after the newest. */
  private slots(): number {
    const m = model.peek()
    return m ? m.commits.length + (m.changed.length > 0 ? 1 : 0) : 0
  }

  private indexAt(clientX: number): number | null {
    const n = this.slots()
    if (n === 0) return null
    const x = clientX - this.canvas.getBoundingClientRect().left
    return Math.max(0, Math.min(n - 1, Math.floor((x / this.width) * n)))
  }

  private draw() {
    this.frame = 0
    const { ctx, palette } = this
    ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0)
    ctx.clearRect(0, 0, this.width, this.height)
    const commits = model.peek()?.commits ?? []
    const n = commits.length
    const slots = this.slots()
    if (slots === 0) return

    const [from, to] = range.peek()
    const barWidth = this.width / slots
    ctx.fillStyle = palette.frames[0]
    ctx.fillRect(from * barWidth, 0, (to - from) * barWidth, this.height)

    let maxFiles = 1
    for (const c of commits) maxFiles = Math.max(maxFiles, c.files.length + c.other)
    const scale = Math.log2(1 + maxFiles)
    const gap = barWidth >= 4 ? 1 : 0
    const chosen = selectedCommit.peek()

    for (let i = 0; i < n; i++) {
      const c = commits[i]
      const h = 3 + (this.height - 7) * (Math.log2(1 + c.files.length + c.other) / scale)
      const inRange = i >= from && i < to
      ctx.fillStyle = i === chosen || i === this.hover ? palette.ink : inRange ? palette.activity[3] : palette.ink3
      ctx.globalAlpha = inRange || i === chosen || i === this.hover ? 1 : 0.45
      ctx.fillRect(i * barWidth + gap / 2, this.height - h, Math.max(0.6, barWidth - gap), h)
    }
    ctx.globalAlpha = 1

    const changed = model.peek()?.changed.length ?? 0
    if (changed > 0) {
      const h = 3 + (this.height - 7) * (Math.log2(1 + changed) / scale)
      ctx.fillStyle = chosen === 'working' || this.hover === n ? palette.ink : palette.change
      ctx.fillRect(n * barWidth + gap / 2, this.height - h, Math.max(2, barWidth - gap), h)
    }
  }

  private onDown(e: PointerEvent) {
    const i = this.indexAt(e.clientX)
    if (i === null || e.button !== 0) return
    this.canvas.setPointerCapture(e.pointerId)
    this.drag = { anchor: i, startX: e.clientX, moved: false }
  }

  private onMove(e: PointerEvent) {
    const i = this.indexAt(e.clientX)
    const last = (model.peek()?.commits.length ?? 1) - 1
    if (this.drag && i !== null) {
      if (Math.abs(e.clientX - this.drag.startX) > 3) this.drag.moved = true
      const a = Math.min(this.drag.anchor, last)
      const b = Math.min(i, last)
      if (this.drag.moved && last >= 0) range.value = [Math.min(a, b), Math.max(a, b) + 1]
    }
    this.setHover(i, e.clientX)
  }

  private onUp(e: PointerEvent) {
    const drag = this.drag
    this.drag = null
    if (!drag || drag.moved) return
    const i = this.indexAt(e.clientX)
    if (i === null) return
    const target = i === (model.peek()?.commits.length ?? 0) ? 'working' : i
    selectedCommit.value = selectedCommit.peek() === target ? null : target
  }

  private setHover(i: number | null, clientX = 0) {
    if (this.hover !== i) {
      this.hover = i
      this.request()
    }
    const m = model.peek()
    const c = i === null ? null : m?.commits[i]
    if (i !== null && m && i === m.commits.length && m.changed.length > 0) {
      this.tip.textContent = `Uncommitted · ${format.int(m.changed.length)} files`
    } else if (c) {
      this.tip.textContent = `${c.sha.slice(0, 7)} · ${format.dateShort(format.fromUnix(c.time))} · ${c.subject}`
    } else {
      this.tip.hidden = true
      return
    }
    this.tip.hidden = false
    const host = this.host.getBoundingClientRect()
    const x = Math.max(0, Math.min(clientX - host.left - this.tip.offsetWidth / 2, host.width - this.tip.offsetWidth))
    this.tip.style.transform = `translate(${x}px, ${-this.tip.offsetHeight - 6}px)`
  }
}
