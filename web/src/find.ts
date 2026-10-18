import type { Model, Node } from './model'

// The file finder's matching: every character of a term appears in the path in order, case and
// accents aside, and the best alignment scores highest. Contiguous runs, runs that start a folder
// or a word, and matches in the name rather than the folders score more, much as fzf scores them.
// Spaces separate terms that must all match.

/** A file or folder whose path matches, with the matched positions in its path. */
export interface Found {
  node: Node
  score: number
  positions: number[]
}

const MATCH = 16
const GAP_START = -3
const GAP_EXTEND = -1
/** The start of the path or of a folder or file name. */
const SEGMENT = 10
/** After a space, underscore, hyphen or dot. */
const WORD = 8
/** A capital after a lower-case letter, or a digit after a non-digit. */
const CAMEL = 7
const CONSECUTIVE = 4
const FIRST_CHAR = 2
/** Per character matched in the name rather than its folders. */
const IN_NAME = 2
/** A term found whole in the path, so `multiplayer` beats `plain_layer` for "player". */
const WHOLE = 40
/** A term that is the whole name, or the name without its extension. */
const EXACT = 32

const NONE = -(1 << 29)
const matched = (score: number) => score > NONE / 2

interface Entry {
  node: Node
  /** The path folded for matching, one character for each character of the path. */
  text: string
  bonus: Int8Array
  /** Where the name starts in the path. */
  name: number
  /** The folded name without its extension. */
  stem: string
}

const indexes = new WeakMap<Model, Entry[]>()

function entries(model: Model): Entry[] {
  let list = indexes.get(model)
  if (!list) {
    list = []
    for (const node of model.nodes) {
      if (node === model.root) continue
      const text = fold(node.path)
      const name = node.path.length - node.name.length
      const dot = text.lastIndexOf('.')
      list.push({ node, text, bonus: bonuses(node.path), name, stem: dot > name ? text.slice(name, dot) : text.slice(name) })
    }
    indexes.set(model, list)
  }
  return list
}

/**
 * Lower case without accents, keeping one character for each character so matched positions
 * stay positions in the original path. A backslash reads as the scan's slash.
 */
export function fold(text: string): string {
  let out = ''
  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i)
    if (code < 128) out += code >= 65 && code <= 90 ? String.fromCharCode(code + 32) : code === 92 ? '/' : text[i]
    else out += text[i].toLowerCase().normalize('NFD')[0] ?? text[i]
  }
  return out
}

const isLower = (c: string) => c !== c.toUpperCase()
const isUpper = (c: string) => c !== c.toLowerCase()
const isDigit = (c: string) => c >= '0' && c <= '9'

function bonuses(path: string): Int8Array {
  const bonus = new Int8Array(path.length)
  for (let i = 0; i < path.length; i++) {
    const c = path[i]
    const before = i > 0 ? path[i - 1] : '/'
    bonus[i] = before === '/' ? SEGMENT
      : before === ' ' || before === '_' || before === '-' || before === '.' ? WORD
      : (isLower(before) && isUpper(c)) || (!isDigit(before) && isDigit(c)) ? CAMEL
      : 0
  }
  return bonus
}

// Score, run length and previous matched position for each term character and path position,
// reused between calls.
let scores = new Int32Array(0)
let runs = new Int32Array(0)
let previous = new Int32Array(0)

/**
 * The best alignment of a folded term with an entry's path, or NONE when the term's characters
 * do not all appear in order. With `positions`, also the matched positions.
 */
function align(term: string, entry: Entry, positions: number[] | null): number {
  const text = entry.text
  const m = term.length
  const n = text.length
  let at = 0
  for (let i = 0; i < m; i++) {
    at = text.indexOf(term[i], at) + 1
    if (at === 0) return NONE
  }
  if (scores.length < m * n) {
    scores = new Int32Array(m * n * 2)
    runs = new Int32Array(m * n * 2)
    previous = new Int32Array(m * n * 2)
  }
  const bonus = entry.bonus
  for (let i = 0; i < m; i++) {
    const c = term[i]
    const row = i * n
    // The best score with term[i - 1] matched at k <= j - 2, less the gap's penalty.
    let gap = NONE
    let gapFrom = -1
    for (let j = 0; j < n; j++) {
      if (i > 0 && j >= 2) {
        const opened = scores[row - n + j - 2] + GAP_START
        if (opened >= gap + GAP_EXTEND) {
          gap = opened
          gapFrom = j - 2
        } else {
          gap += GAP_EXTEND
        }
      }
      const cell = row + j
      if (text[j] !== c) {
        scores[cell] = NONE
        continue
      }
      const match = MATCH + (j >= entry.name ? IN_NAME : 0)
      if (i === 0) {
        scores[cell] = match + bonus[j] * FIRST_CHAR
        runs[cell] = 1
        previous[cell] = -1
        continue
      }
      let best = NONE
      let run = 1
      let from = -1
      const diagonal = j > 0 ? scores[cell - n - 1] : NONE
      if (matched(diagonal)) {
        // A run keeps the bonus of the boundary it started at, unless a stronger boundary starts a new one.
        run = runs[cell - n - 1] + 1
        const first = bonus[j - run + 1]
        let b = bonus[j]
        if (b >= WORD && b > first) run = 1
        else b = Math.max(b, first, CONSECUTIVE)
        best = diagonal + match + b
        from = j - 1
      }
      if (matched(gap) && gap + match + bonus[j] > best) {
        best = gap + match + bonus[j]
        run = 1
        from = gapFrom
      }
      scores[cell] = best
      runs[cell] = run
      previous[cell] = from
    }
  }
  let score = NONE
  let end = -1
  const last = (m - 1) * n
  for (let j = 0; j < n; j++) {
    if (scores[last + j] > score) {
      score = scores[last + j]
      end = j
    }
  }
  if (!matched(score)) return NONE
  if (positions) {
    const found: number[] = []
    for (let i = m - 1, j = end; i >= 0 && j >= 0; i--) {
      found.push(j)
      j = previous[i * n + j]
    }
    positions.push(...found.reverse())
  }
  const name = text.slice(entry.name)
  return score + (text.includes(term) ? WHOLE : 0) + (term === name || term === entry.stem ? EXACT : 0)
}

/** Splits a query into folded terms. */
export function terms(query: string): string[] {
  return fold(query).split(/\s+/).filter(Boolean)
}

/**
 * The project's files and folders that match the query, best first: by score, then those with
 * area under the footprint's `weight` (so Godot's `.uid` sidecars follow their scripts), then
 * shorter path, then path. `total` counts every match.
 */
export function find(model: Model, query: string, limit = 50, weight: (n: Node) => number = () => 1): { found: Found[]; total: number } {
  const parts = terms(query)
  if (parts.length === 0) return { found: [], total: 0 }
  const hits: { entry: Entry; score: number; area: boolean }[] = []
  for (const entry of entries(model)) {
    let score = 0
    for (const term of parts) {
      const s = align(term, entry, null)
      if (!matched(s)) {
        score = NONE
        break
      }
      score += s
    }
    if (matched(score)) hits.push({ entry, score, area: weight(entry.node) > 0 })
  }
  hits.sort((a, b) =>
    b.score - a.score || Number(b.area) - Number(a.area) || a.entry.text.length - b.entry.text.length ||
    (a.entry.node.path < b.entry.node.path ? -1 : 1))
  const found = hits.slice(0, limit).map(({ entry, score }) => {
    const positions: number[] = []
    for (const term of parts) align(term, entry, positions)
    return { node: entry.node, score, positions: [...new Set(positions)].sort((a, b) => a - b) }
  })
  return { found, total: hits.length }
}
