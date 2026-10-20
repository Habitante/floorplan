import { codeFolding, foldEffect, foldedRanges } from '@codemirror/language'
import { EditorState } from '@codemirror/state'
import { describe, expect, it } from 'vitest'
import { changesIn, foldRange, foldsHiding, lineChanges, skeletonFolds, type FileOutline } from './skeleton'

const lines = (n: number) => Array.from({ length: n }, (_, i) => `line ${i + 1}`).join('\n')

describe('uncommitted changes by line', () => {
  it('marks changed and added lines of the working file, and where lines were deleted', () => {
    expect(lineChanges('a\nb\nc\n', 'a\nB\nc\n')).toEqual([{ from: 2, to: 2, removed: 1 }])
    expect(lineChanges('a\nc\n', 'a\nb\nc\n')).toEqual([{ from: 2, to: 2, removed: 0 }])
    // Only removed: nothing to bar, a wedge before line 2.
    expect(lineChanges('a\nb\nc\n', 'a\nc\n')).toEqual([{ from: 2, to: 1, removed: 1 }])
  })

  it('treats a file new at its path as all new, without the empty line after its last newline', () => {
    expect(lineChanges('', 'x\ny\n')).toEqual([{ from: 1, to: 2, removed: 0 }])
    expect(lineChanges('', '')).toEqual([])
    expect(lineChanges('a\n', 'a\n')).toEqual([])
  })

  it('counts what a fold hides: added lines inside it, and removed lines where their change starts', () => {
    const changes = [{ from: 2, to: 4, removed: 1 }, { from: 8, to: 7, removed: 3 }, { from: 10, to: 10, removed: 0 }]
    expect(changesIn(changes, { from: 3, to: 8 })).toEqual({ added: 2, removed: 3 })
    expect(changesIn(changes, { from: 11, to: 20 })).toEqual({ added: 0, removed: 0 })
  })
})

describe('the skeleton', () => {
  const outline: FileOutline = { language: 'gdscript', lines: 10, symbols: [], folds: [{ from: 3, to: 5 }, { from: 7, to: 9 }] }

  it('folds from the end of the line before a fold to the end of its last line', () => {
    const doc = EditorState.create({ doc: lines(10) }).doc
    expect(foldRange(doc, { from: 3, to: 5 })).toEqual({ from: doc.line(2).to, to: doc.line(5).to })
    expect(foldRange(doc, { from: 1, to: 5 })).toBeNull()      // nothing to hang it on
    expect(foldRange(doc, { from: 9, to: 12 })).toBeNull()     // the file changed since its outline
  })

  it('keeps lines an agent points at in view', () => {
    const doc = EditorState.create({ doc: lines(10) }).doc
    expect(skeletonFolds(doc, outline, [[8, 8]])).toEqual([{ from: doc.line(2).to, to: doc.line(5).to }])
    expect(skeletonFolds(doc, outline, [])).toHaveLength(2)
  })

  it('finds the folds that hide given lines', () => {
    let state = EditorState.create({ doc: lines(10), extensions: codeFolding() })
    state = state.update({ effects: skeletonFolds(state.doc, outline, []).map(r => foldEffect.of(r)) }).state
    expect(foldedRanges(state).size).toBe(2)
    expect(foldsHiding(state, [[4, 4]])).toEqual([{ from: state.doc.line(2).to, to: state.doc.line(5).to }])
    expect(foldsHiding(state, [[2, 2], [6, 6], [10, 10]])).toEqual([])     // the lines around folds stay visible
  })
})
