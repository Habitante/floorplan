import { batch, effect } from '@preact/signals'
import { applyPointer, boardView, commonFolder, pointedNodes } from './board'
import { send } from './bridge'
import {
  focus, footprint, lens, model, openReader, pointers, range, reader, readerFocus, readerLines, reveal, selected, selectedCommit,
  theme, zoomTo, type Pointer,
} from './state'
import type { PointerItem } from './types'

export function receivePointer(message: { agent: string; caption: string; items: PointerItem[]; at: number; reveal: boolean }) {
  const pointer: Pointer = { agent: message.agent, caption: message.caption, items: message.items, at: message.at, arrived: performance.now() }
  pointers.value = applyPointer(pointers.peek(), pointer)
  if (message.reveal) goTo(pointer)
}

export function dismissPointer(agent: string) {
  pointers.value = pointers.peek().filter(p => p.agent !== agent)
}

/**
 * Brings a pointer into view: the map shows everything it names (one item is selected, several
 * show their common folder), and the reader opens at the first lines it points at.
 */
export function goTo(pointer: Pointer) {
  const m = model.peek()
  if (!m) return
  const nodes = pointedNodes(m, [pointer])
  const lines = pointer.items.find(i => i.from !== undefined && !i.note && m.byPath.get(i.path)?.isFile)
  batch(() => {
    if (nodes.length === 1 && !lines) reveal(nodes[0])
    else if (nodes.length > 1) zoomTo(commonFolder(nodes))
    if (lines) {
      readerFocus.value = { path: lines.path, from: lines.from!, to: lines.to ?? lines.from! }
      openReader(m.byPath.get(lines.path)!)
    }
  })
}

/** Reports the view to the host whenever it settles on something new. */
export function reportBoard(delay = 150): () => void {
  let sent = ''
  let timer: ReturnType<typeof setTimeout> | undefined
  const stop = effect(() => {
    const m = model.value
    if (!m) return
    const view = boardView(m, {
      focus: focus.value,
      selected: selected.value,
      commit: selectedCommit.value,
      range: range.value,
      reader: reader.value,
      lines: readerLines.value,
      area: footprint.value,
      fill: lens.value,
      theme: theme.value,
    })
    const json = JSON.stringify(view)
    clearTimeout(timer)
    if (json === sent) return
    timer = setTimeout(() => {
      sent = json
      send({ type: 'board', view })
    }, delay)
  })
  return () => {
    clearTimeout(timer)
    stop()
  }
}
