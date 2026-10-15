import { render } from 'preact'
import { connect, send } from './bridge'
import { App } from './components'
import { loadIconFonts } from './icons'
import { zoomOut } from './map-controller'
import { applyScan, model, openReader, reader, selected, selectedCommit, status } from './state'
import './styles.css'

const theme = new URLSearchParams(location.search).get('theme')
if (theme === 'light' || theme === 'dark') document.documentElement.dataset.theme = theme

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
      applyScan(message.scan)
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
    case 'idle':
      status.value = { state: 'idle' }
      break
  }
})

addEventListener('keydown', e => {
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
