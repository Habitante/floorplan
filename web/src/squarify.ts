export interface Rect {
  x: number
  y: number
  w: number
  h: number
}

/**
 * Squarified treemap layout (Bruls, Huizing & van Wijk, 2000). Weights must be positive and
 * sorted in descending order; the result is in the same order and fills `rect` exactly.
 */
export function squarify(weights: readonly number[], rect: Rect): Rect[] {
  const out: Rect[] = new Array(weights.length)
  let total = 0
  for (const w of weights) total += w
  if (total <= 0 || rect.w <= 0 || rect.h <= 0) return weights.map(() => ({ x: rect.x, y: rect.y, w: 0, h: 0 }))

  const scale = (rect.w * rect.h) / total
  let { x, y, w, h } = rect
  let i = 0
  while (i < weights.length) {
    const side = Math.min(w, h)
    let sum = 0
    let min = Infinity
    let max = 0
    let worst = Infinity
    let j = i
    // Grow the row while its worst aspect ratio keeps improving.
    while (j < weights.length) {
      const area = weights[j] * scale
      const s = sum + area
      const mn = Math.min(min, area)
      const mx = Math.max(max, area)
      const ratio = Math.max((side * side * mx) / (s * s), (s * s) / (side * side * mn))
      if (j > i && ratio > worst) break
      worst = ratio
      sum = s
      min = mn
      max = mx
      j++
    }

    const last = j >= weights.length
    // The final row takes all remaining space, absorbing floating-point drift.
    const thickness = last ? (w >= h ? w : h) : sum / side
    let offset = 0
    for (let k = i; k < j; k++) {
      const length = k === j - 1 ? side - offset : (weights[k] * scale) / thickness
      out[k] = w >= h
        ? { x, y: y + offset, w: thickness, h: length }
        : { x: x + offset, y, w: length, h: thickness }
      offset += length
    }
    if (w >= h) {
      x += thickness
      w -= thickness
    } else {
      y += thickness
      h -= thickness
    }
    i = j
  }
  return out
}
