import { effect } from '@preact/signals'
import { applyPointer, boardView, commonFolder, pointedNodes } from './board'
import { send } from './bridge'
import {
  focus, footprint, lens, model, pointers, range, reader, readerLines, reveal, selected, selectedCommit, zoomTo, type Pointer,
} from './state'

export function receivePointer(message: { agent: string; caption: string; paths: string[]; at: number }) {
  pointers.value = applyPointer(pointers.peek(), { ...message, arrived: performance.now() })
}

export function dismissPointer(agent: string) {
  pointers.value = pointers.peek().filter(p => p.agent !== agent)
}

/** Brings a pointer into view: one item is revealed and selected, several show their common folder. */
export function goTo(pointer: Pointer) {
  const m = model.peek()
  if (!m) return
  const nodes = pointedNodes(m, [pointer])
  if (nodes.length === 1) reveal(nodes[0])
  else zoomTo(commonFolder(nodes))
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
