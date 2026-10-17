import type { BoardView, PointerItem, Scan } from './types'

export type HostMessage =
  | { type: 'scanning'; path: string }
  | { type: 'refreshing' }
  | { type: 'refreshed' }
  | { type: 'progress'; phase: string; done: number; total: number }
  | { type: 'scan'; scan: Scan }
  | { type: 'error'; message: string; path?: string }
  | { type: 'idle' }
  | { type: 'file' | 'diff'; request: number }
  | { type: 'openReader'; path: string; commit: string | null }
  /** An agent points at paths of the scan (validated by the host); no items clears its pointer. */
  | { type: 'pointer'; agent: string; caption: string; items: PointerItem[]; at: number; reveal: boolean }

export type AppMessage =
  | { type: 'ready' }
  | { type: 'open' }
  | { type: 'rescan' }
  | { type: 'scope'; untracked: boolean }
  | { type: 'rendered'; files: number; items: number; ms: number }
  | { type: 'readFile'; path: string }
  | { type: 'readDiff'; path: string; commit: string }
  | { type: 'board'; view: BoardView }

interface WebView {
  postMessage(message: unknown): void
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void
}

const webview = (window as unknown as { chrome?: { webview?: WebView } }).chrome?.webview

/** True inside the desktop window; false in a browser preview. */
export const inHost = webview !== undefined

export function send(message: AppMessage) {
  webview?.postMessage(message)
}

let nextRequest = 1
const waiting = new Map<number, (reply: unknown) => void>()

/** Sends a message that the host answers with a reply carrying the same request id. */
export function request<T>(message: AppMessage): Promise<T> {
  if (!webview) return Promise.reject(new Error('Files are read by the desktop app; not available in the browser preview'))
  const id = nextRequest++
  return new Promise<T>(resolve => {
    waiting.set(id, reply => resolve(reply as T))
    webview.postMessage({ ...message, request: id })
  })
}

export function connect(receive: (message: HostMessage) => void) {
  if (webview) {
    webview.addEventListener('message', event => {
      const data = event.data as { request?: number }
      const answer = data.request !== undefined ? waiting.get(data.request) : undefined
      if (answer) {
        waiting.delete(data.request!)
        answer(data)
      } else {
        receive(event.data as HostMessage)
      }
    })
    send({ type: 'ready' })
    return
  }
  // Browser preview during development: a scan saved by `floorplan scan`.
  receive({ type: 'scanning', path: 'preview' })
  fetch('/__dev/scan.json')
    .then(r => (r.ok ? r.json() : Promise.reject(new Error(`No saved scan at web/.dev/scan.json (${r.status})`))))
    .then(
      (scan: Scan) => receive({ type: 'scan', scan }),
      (e: Error) => receive({ type: 'error', message: e.message }),
    )
}
