import { render } from 'preact'
import { receivePointer, reportBoard } from './board-controller'
import { connect, send } from './bridge'
import { App } from './components'
import { loadIconFonts } from './icons'
import { zoomOut } from './map-controller'
import { watchRelations } from './relations'
import { applyScan, finding, model, openReader, reader, receiveSince, selected, selectedCommit, status, theme } from './state'
import './styles.css'

const forced = new URLSearchParams(location.search).get('theme')
if (forced === 'light' || forced === 'dark') document.documentElement.dataset.theme = forced
const dark = matchMedia('(prefers-color-scheme: dark)')
const followTheme = () => {
  theme.value = forced === 'light' || forced === 'dark' ? forced : dark.matches ? 'dark' : 'light'
}
dark.addEventListener('change', followTheme)
followTheme()

loadIconFonts()
render(<App />, document.getElementById('app')!)

connect(message => {
  switch (message.type) {
    case 'scanning':
      status.value = { state: 'scanning', path: message.path }
      break
    case 'refreshing':
      status.value = { ...status.peek(), refreshing: true }
      break
    case 'refreshed':
      status.value = { ...status.peek(), refreshing: false }
      break
    case 'progress':
      status.value = { ...status.peek(), state: 'scanning', phase: message.phase, done: message.done, total: message.total }
      break
    case 'scan':
      applyScan(message.scan, message.since)
      break
    case 'since':
      receiveSince(message.since)
      break
    case 'error':
      status.value = { state: 'error', message: message.message, path: message.path }
      break
    case 'openReader': {
      const m = model.peek()
      const node = m?.byPath.get(message.path)
      if (!m || !node) break
      const index = message.commit && message.commit !== 'working' ? m.commits.findIndex(c => c.sha.startsWith(message.commit!)) : -1
      openReader(node, message.commit === 'working' ? 'working' : index >= 0 ? index : null)
      break
    }
    case 'pointer':
      receivePointer(message)
      break
    case 'idle':
      status.value = { state: 'idle' }
      break
  }
})
reportBoard()
watchRelations()

// The human's own input tells the host they are here; view changes do not, since refreshes and
// agents make them too. Chromium repeats pointer events when content changes under a resting
// cursor, so a pointer counts only when it moves.
const PRESENCE_EVERY_MS = 10_000
let lastPresence = -Infinity
let pointerAt = { x: NaN, y: NaN }
const present = (e: Event) => {
  if (e.type === 'pointermove') {
    const { screenX: x, screenY: y } = e as PointerEvent
    if (Math.abs(x - pointerAt.x) < 2 && Math.abs(y - pointerAt.y) < 2) return
    pointerAt = { x, y }
  }
  const now = performance.now()
  if (now - lastPresence < PRESENCE_EVERY_MS) return
  lastPresence = now
  send({ type: 'presence' })
}
for (const type of ['pointermove', 'pointerdown', 'wheel', 'keydown']) addEventListener(type, present, { capture: true, passive: true })

addEventListener('keydown', e => {
  if (e.ctrlKey && !e.altKey && !e.shiftKey && e.key.toLowerCase() === 'p') {
    // The file finder, which also keeps the browser's print dialog away.
    e.preventDefault()
    if (model.peek()) finding.value = true
    return
  }
  const typing = e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement
  if (typing) return
  if (e.key === 'F5' || (e.ctrlKey && e.key.toLowerCase() === 'r')) {
    e.preventDefault()
    send({ type: 'rescan' })
  } else if (e.ctrlKey && e.key.toLowerCase() === 'o') {
    e.preventDefault()
    send({ type: 'open' })
  } else if (e.key === 'Backspace' || (e.altKey && e.key === 'ArrowLeft')) {
    e.preventDefault()
    zoomOut()
  } else if (e.key === 'Escape') {
    if (reader.peek()) reader.value = null
    else if (selectedCommit.peek() !== null) selectedCommit.value = null
    else selected.value = null
  }
})
