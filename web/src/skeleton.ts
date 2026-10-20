import { codeFolding, foldEffect, foldedRanges, foldGutter, foldKeymap, foldService, unfoldEffect } from '@codemirror/language'
import { Chunk } from '@codemirror/merge'
import { Facet, Text, type EditorState, type Extension, type StateEffect } from '@codemirror/state'
import { EditorView, gutter, GutterMarker, keymap, ViewPlugin, type ViewUpdate } from '@codemirror/view'
import * as format from './format'
import { GLYPHS } from './glyphs'

// The reader's skeleton: a file folded to its declarations, with what changed since the last commit
// marked in its margin. Outlines come from the host; changes come from the same diff as the reader's
// diff view, so both show the same lines.

export interface LineRange {
  from: number
  to: number
}

/** A declaration in a file, or a section; lines count from 1 and include both ends. */
export interface OutlineSymbol {
  id?: string
  kind: string
  name: string
  /** The first line, with the comments that belong to it. */
  from: number
  /** The line the declaration starts on. */
  line: number
  to: number
  children?: OutlineSymbol[]
}

/** What a file declares, and the lines its skeleton folds away. */
export interface FileOutline {
  language: string
  lines: number
  symbols: OutlineSymbol[]
  folds: LineRange[]
  errors?: number
}

/**
 * A change since the last commit, in lines of the working file: from-to were added or changed (to is
 * from − 1 when lines were only removed, just before line from), and removed counts the lines it took out.
 */
export interface LineChange {
  from: number
  to: number
  removed: number
}

/** Lines in agents' pointers, which the skeleton keeps open and the overview strip marks. */
export const pointedLines = Facet.define<readonly (readonly [number, number])[], readonly (readonly [number, number])[]>({
  combine: values => values.flat(),
})

const toText = (text: string) => Text.of(text.split(/\r\n?|\n/))

/** The text's last line: a final newline leaves an empty line that Git does not count. */
function lastLine(doc: Text): number {
  return doc.lines > 1 && doc.line(doc.lines).length === 0 ? doc.lines - 1 : doc.lines
}

/** What changed between the committed text and the working one; an empty committed text makes every line new. */
export function lineChanges(before: string, after: string): LineChange[] {
  const a = toText(before), b = toText(after)
  if (before.length === 0) return after.length === 0 ? [] : [{ from: 1, to: lastLine(b), removed: 0 }]
  const line = (doc: Text, position: number) => doc.lineAt(Math.min(position, doc.length)).number
  return Chunk.build(a, b).map(chunk => {
    const removed = chunk.toA > chunk.fromA ? line(a, chunk.toA - 1) - line(a, chunk.fromA) + 1 : 0
    if (chunk.toB > chunk.fromB) return { from: line(b, chunk.fromB), to: line(b, chunk.toB - 1), removed }
    const at = chunk.fromB > b.length ? b.lines + 1 : line(b, chunk.fromB)
    return { from: at, to: at - 1, removed }
  })
}

/** Lines added and removed inside a range of lines; a change's removed lines count where it starts. */
export function changesIn(changes: readonly LineChange[], range: LineRange): { added: number; removed: number } {
  let added = 0, removed = 0
  for (const change of changes) {
    const low = Math.max(change.from, range.from), high = Math.min(change.to, range.to)
    if (high >= low) added += high - low + 1
    if (change.from >= range.from && change.from <= range.to) removed += change.removed
  }
  return { added, removed }
}

/** A fold of lines as document positions: from the end of the line before it to the end of its last line. */
export function foldRange(doc: Text, fold: LineRange): { from: number; to: number } | null {
  if (fold.from < 2 || fold.to < fold.from || fold.to > doc.lines) return null
  return { from: doc.line(fold.from - 1).to, to: doc.line(fold.to).to }
}

/** The lines a fold at these positions hides. */
function hiddenLines(doc: Text, from: number, to: number): LineRange {
  return { from: doc.lineAt(from).number + 1, to: doc.lineAt(to).number }
}

const overlaps = (range: LineRange, lines: readonly (readonly [number, number])[]) => lines.some(([from, to]) => from <= range.to && to >= range.from)

/** The outline's folds as positions, except those holding lines that should stay in view. */
export function skeletonFolds(doc: Text, outline: FileOutline, keepOpen: readonly (readonly [number, number])[]): { from: number; to: number }[] {
  const folds: { from: number; to: number }[] = []
  for (const fold of outline.folds) {
    const range = foldRange(doc, fold)
    if (range && !overlaps(fold, keepOpen)) folds.push(range)
  }
  return folds
}

/** The folds that hide any of these lines. */
export function foldsHiding(state: EditorState, lines: readonly (readonly [number, number])[]): { from: number; to: number }[] {
  const doc = state.doc
  const folds: { from: number; to: number }[] = []
  foldedRanges(state).between(0, doc.length, (from, to) => {
    if (overlaps(hiddenLines(doc, from, to), lines)) folds.push({ from, to })
  })
  return folds
}

/** Folds the file to its skeleton, keeping these lines in view. */
export function foldSkeleton(view: EditorView, outline: FileOutline, keepOpen: readonly (readonly [number, number])[]) {
  const effects: StateEffect<unknown>[] = skeletonFolds(view.state.doc, outline, keepOpen).map(range => foldEffect.of(range))
  if (effects.length) view.dispatch({ effects })
}

/** Opens the folds that hide any of these lines. */
export function revealLines(view: EditorView, lines: readonly (readonly [number, number])[]) {
  const effects: StateEffect<unknown>[] = foldsHiding(view.state, lines).map(range => unfoldEffect.of(range))
  if (effects.length) view.dispatch({ effects })
}

const foldsChanged = (update: ViewUpdate) => foldedRanges(update.startState) !== foldedRanges(update.state)

function placeholder(onclick: (event: Event) => void, prepared: { lines: number; added: number; removed: number }): HTMLElement {
  const element = document.createElement('span')
  element.className = 'cm-fold-placeholder'
  const changes = [prepared.added ? `+${format.int(prepared.added)}` : '', prepared.removed ? `−${format.int(prepared.removed)}` : ''].filter(Boolean).join(' ')
  element.title = 'Show these lines'
  element.setAttribute('aria-label', `${format.int(prepared.lines)} lines folded${changes ? `, uncommitted changes ${changes}` : ''}; show them`)
  element.textContent = `⋯ ${format.int(prepared.lines)}`
  if (changes) {
    const change = document.createElement('span')
    change.className = 'cm-fold-change'
    change.textContent = changes
    element.append(change)
  }
  element.onclick = onclick
  return element
}

function chevron(open: boolean): HTMLElement {
  const element = document.createElement('span')
  element.className = 'icon cm-fold-chevron'
  element.title = open ? 'Fold' : 'Show these lines'
  element.textContent = open ? GLYPHS.unfolded : GLYPHS.folded
  return element
}

/** Folding from the outline: its folds, a placeholder with each fold's size and changes, and a gutter to refold. */
export function outlineFolding(outline: FileOutline, changes: readonly LineChange[]): Extension {
  const byLine = new Map<number, LineRange>()
  for (const fold of outline.folds) byLine.set(fold.from - 1, fold)
  return [
    foldService.of((state: EditorState, lineStart: number) => {
      const fold = byLine.get(state.doc.lineAt(lineStart).number)
      return fold ? foldRange(state.doc, fold) : null
    }),
    codeFolding({
      preparePlaceholder: (state, range) => {
        const lines = hiddenLines(state.doc, range.from, range.to)
        return { lines: lines.to - lines.from + 1, ...changesIn(changes, lines) }
      },
      placeholderDOM: (_view, onclick, prepared) => placeholder(onclick, prepared),
    }),
    foldGutter({ markerDOM: chevron }),
    keymap.of(foldKeymap),
  ]
}

class ChangeMark extends GutterMarker {
  constructor(readonly changed: boolean, readonly deleted: boolean, readonly deletedAfter: boolean) {
    super()
  }

  eq(other: ChangeMark) {
    return other.changed === this.changed && other.deleted === this.deleted && other.deletedAfter === this.deletedAfter
  }

  toDOM() {
    const mark = document.createElement('span')
    mark.className = ['cm-change-mark', this.changed && 'changed', this.deleted && 'deleted', this.deletedAfter && 'deleted-after'].filter(Boolean).join(' ')
    mark.title = this.changed ? 'Changed since the last commit' : 'Lines deleted here since the last commit'
    return mark
  }
}

/** A violet bar beside lines changed since the last commit, and a wedge where lines were deleted; a fold shows its own. */
function changeGutter(changes: readonly LineChange[]): Extension {
  return gutter({
    class: 'cm-change-gutter',
    lineMarker(view, block) {
      const doc = view.state.doc
      const first = doc.lineAt(block.from).number, last = doc.lineAt(block.to).number
      // A folded line's block runs to the end of the lines it hides: what changed inside them bars the fold.
      let changed = false, deleted = false, deletedAfter = false
      for (const change of changes) {
        if (change.to >= change.from) {
          if (change.from <= last && change.to >= first) changed = true
        } else if (change.from === first) deleted = true
        else if (change.from > first && change.from <= last) changed = true
        else if (change.from === last + 1 && last >= lastLine(doc)) deletedAfter = true
      }
      return changed || deleted || deletedAfter ? new ChangeMark(changed, deleted, deletedAfter) : null
    },
    lineMarkerChange: update => update.docChanged || foldsChanged(update),
    // Keeps the margin's width whether or not changes are in view, so the text never shifts.
    initialSpacer: () => new ChangeMark(false, false, false),
  })
}

/** A strip along the scrollbar marking where the file's changes and agents' pointers are; a mark scrolls there. */
function overview(changes: readonly LineChange[]): Extension {
  return ViewPlugin.fromClass(class {
    readonly strip = document.createElement('div')

    constructor(readonly view: EditorView) {
      this.strip.className = 'cm-overview'
      this.strip.setAttribute('aria-hidden', 'true')
      view.dom.appendChild(this.strip)
      this.schedule()
    }

    update(update: ViewUpdate) {
      if (update.geometryChanged || update.heightChanged || foldsChanged(update)
        || update.startState.facet(pointedLines) !== update.state.facet(pointedLines)) this.schedule()
    }

    schedule() {
      this.view.requestMeasure({ read: view => this.measure(view), write: marks => this.draw(marks) })
    }

    measure(view: EditorView) {
      const doc = view.state.doc
      const height = Math.max(1, view.contentHeight)
      const at = (from: number, to: number, kind: string) => {
        const top = view.lineBlockAt(doc.line(Math.min(Math.max(1, from), doc.lines)).from)
        const bottom = view.lineBlockAt(doc.line(Math.min(Math.max(1, to), doc.lines)).from)
        return { top: top.top / height, height: (bottom.bottom - top.top) / height, line: Math.min(Math.max(1, from), doc.lines), kind }
      }
      return [
        ...changes.map(c => at(c.from, Math.max(c.from, c.to), 'change')),
        ...view.state.facet(pointedLines).map(([from, to]) => at(from, to, 'pointer')),
      ]
    }

    draw(marks: { top: number; height: number; line: number; kind: string }[]) {
      this.strip.replaceChildren(...marks.map(mark => {
        const element = document.createElement('div')
        element.className = `mark ${mark.kind}`
        element.style.top = `${(mark.top * 100).toFixed(3)}%`
        element.style.height = `${(mark.height * 100).toFixed(3)}%`
        element.onclick = () => {
          revealLines(this.view, [[mark.line, mark.line]])
          this.view.dispatch({ effects: EditorView.scrollIntoView(this.view.state.doc.line(mark.line).from, { y: 'center' }) })
        }
        return element
      }))
    }

    destroy() {
      this.strip.remove()
    }
  })
}

/** The margin's marks for what changed since the last commit, and the overview strip. */
export function changeMarks(changes: readonly LineChange[]): Extension {
  return [changeGutter(changes), overview(changes)]
}
