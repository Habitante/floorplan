import type { FileKind } from './types'

/** Canvas colours, read from the CSS tokens so both themes stay defined in one place. */
export interface Palette {
  surface: string
  /** The map's background, showing in the gutters between folders. */
  gutter: string
  ink: string
  ink2: string
  ink3: string
  /** Folder frames: regions one level below the focus, and deeper folders. */
  region: string
  folder: string
  /** Steps of the warm ramp shared by activity and age; index 0 is the neutral zero. */
  heat: string[]
  kind: Record<FileKind, string>
  /** Dim tint of each kind, for what the activity and age lenses leave at zero. */
  ground: Record<FileKind, string>
  /** Outline for uncommitted changes. */
  change: string
  /** Outline for agents' pointers. */
  pointer: string
  /** The systems lens's five colours, by slot (see systemSlot). */
  systems: string[]
  unsampled: CanvasPattern | string
}

export function readPalette(ctx: CanvasRenderingContext2D): Palette {
  const style = getComputedStyle(document.documentElement)
  const v = (name: string) => style.getPropertyValue(name).trim()
  const zero = v('--zero')
  const kinds = (prefix: string): Record<FileKind, string> => {
    const other = v(`--${prefix}-other`)
    return { code: v(`--${prefix}-code`), test: v(`--${prefix}-test`), docs: v(`--${prefix}-docs`), asset: v(`--${prefix}-asset`), data: other, other }
  }
  return {
    surface: v('--surface'),
    gutter: v('--gutter'),
    ink: v('--ink'),
    ink2: v('--ink-2'),
    ink3: v('--ink-3'),
    region: v('--frame-region'),
    folder: v('--frame-folder'),
    heat: [zero, v('--act-1'), v('--act-2'), v('--act-3'), v('--act-4'), v('--act-5')],
    kind: kinds('kind'),
    ground: kinds('ground'),
    change: v('--change'),
    pointer: v('--pointer'),
    systems: [1, 2, 3, 4, 5].map(i => v(`--sys-${i}`)),
    unsampled: hatch(ctx, zero, v('--hatch')),
  }
}

function hatch(ctx: CanvasRenderingContext2D, base: string, line: string): CanvasPattern | string {
  const tile = document.createElement('canvas')
  tile.width = tile.height = 6
  const t = tile.getContext('2d')
  if (!t) return base
  t.fillStyle = base
  t.fillRect(0, 0, 6, 6)
  t.strokeStyle = line
  t.lineWidth = 1.5
  t.beginPath()
  t.moveTo(-1, 7)
  t.lineTo(7, -1)
  t.stroke()
  return ctx.createPattern(tile, 'repeat') ?? base
}

const inkCache = new Map<string, boolean>()

/** True when dark text reads better than light text on this fill. */
export function prefersDarkText(fill: string): boolean {
  let dark = inkCache.get(fill)
  if (dark === undefined) {
    const m = /^#([0-9a-f]{6})$/i.exec(fill)
    if (!m) return true
    const n = parseInt(m[1], 16)
    const lin = (c: number) => {
      const s = c / 255
      return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
    }
    const luminance = 0.2126 * lin(n >> 16) + 0.7152 * lin((n >> 8) & 255) + 0.0722 * lin(n & 255)
    // Compare contrast against near-black and white text.
    dark = (luminance + 0.05) / 0.0534 >= 1.05 / (luminance + 0.05)
    inkCache.set(fill, dark)
  }
  return dark
}
