import { javascript } from '@codemirror/lang-javascript'
import { markdown } from '@codemirror/lang-markdown'
import { HighlightStyle, StreamLanguage, syntaxHighlighting, type StreamParser } from '@codemirror/language'
import { c, cpp, csharp, dart, java, kotlin, shader } from '@codemirror/legacy-modes/mode/clike'
import { css } from '@codemirror/legacy-modes/mode/css'
import { go } from '@codemirror/legacy-modes/mode/go'
import { json } from '@codemirror/legacy-modes/mode/javascript'
import { lua } from '@codemirror/legacy-modes/mode/lua'
import { powerShell } from '@codemirror/legacy-modes/mode/powershell'
import { properties } from '@codemirror/legacy-modes/mode/properties'
import { python } from '@codemirror/legacy-modes/mode/python'
import { rust } from '@codemirror/legacy-modes/mode/rust'
import { shell } from '@codemirror/legacy-modes/mode/shell'
import { toml } from '@codemirror/legacy-modes/mode/toml'
import { html, xml } from '@codemirror/legacy-modes/mode/xml'
import { yaml } from '@codemirror/legacy-modes/mode/yaml'
import { unifiedMergeView } from '@codemirror/merge'
import { EditorState, type Extension } from '@codemirror/state'
import { EditorView, highlightSpecialChars, lineNumbers } from '@codemirror/view'
import { tags as t } from '@lezer/highlight'
import { useEffect, useRef, useState } from 'preact/hooks'
import { request } from './bridge'
import * as format from './format'
import { Icon } from './icons'
import { displayName, type Model } from './model'
import { activity, range, reader, readerLines, type ReaderTarget } from './state'

/** GDScript reads well as Python once its own declaration words count as keywords. */
const GDSCRIPT_WORDS = new Set([
  'func', 'var', 'const', 'extends', 'signal', 'class_name', 'enum', 'match', 'preload', 'await',
  'static', 'export', 'onready', 'tool', 'setget', 'yield', 'self', 'super', 'void', 'when',
])
const gdscript: StreamParser<unknown> = {
  ...(python as StreamParser<unknown>),
  name: 'gdscript',
  token(stream, state) {
    const style = python.token(stream, state)
    return style === 'variable' && GDSCRIPT_WORDS.has(stream.current()) ? 'keyword' : style
  },
}

const stream = (parser: StreamParser<unknown>): Extension => StreamLanguage.define(parser)

function languageFor(path: string): Extension {
  const name = path.toLowerCase().split('/').pop() ?? ''
  const extension = name.includes('.') ? name.slice(name.lastIndexOf('.')) : name
  switch (extension) {
    case '.gd': return stream(gdscript)
    case '.py': return stream(python as StreamParser<unknown>)
    case '.cs': return stream(csharp as StreamParser<unknown>)
    case '.c': case '.h': return stream(c as StreamParser<unknown>)
    case '.cpp': case '.cc': case '.cxx': case '.hpp': case '.hh': case '.hxx': case '.inl': return stream(cpp as StreamParser<unknown>)
    case '.java': return stream(java as StreamParser<unknown>)
    case '.kt': case '.kts': return stream(kotlin as StreamParser<unknown>)
    case '.dart': return stream(dart as StreamParser<unknown>)
    case '.gdshader': case '.gdshaderinc': case '.shader': case '.glsl': case '.vert': case '.frag': case '.comp': case '.hlsl':
      return stream(shader as StreamParser<unknown>)
    case '.js': case '.mjs': case '.cjs': case '.jsx': return javascript({ jsx: true })
    case '.ts': case '.mts': case '.cts': return javascript({ typescript: true })
    case '.tsx': return javascript({ typescript: true, jsx: true })
    case '.md': case '.markdown': return markdown()
    case '.json': case '.jsonc': return stream(json as StreamParser<unknown>)
    case '.css': case '.scss': case '.less': return stream(css as StreamParser<unknown>)
    case '.html': case '.htm': return stream(html as StreamParser<unknown>)
    case '.xml': case '.csproj': case '.props': case '.targets': case '.svg': case '.slnx': return stream(xml as StreamParser<unknown>)
    case '.yaml': case '.yml': return stream(yaml as StreamParser<unknown>)
    case '.toml': case '.tscn': case '.tres': case '.godot': case '.import': case '.cfg': return stream(toml as StreamParser<unknown>)
    case '.ini': case '.properties': case '.editorconfig': case '.gitignore': case '.gitattributes': return stream(properties as StreamParser<unknown>)
    case '.lua': return stream(lua as StreamParser<unknown>)
    case '.rs': return stream(rust as StreamParser<unknown>)
    case '.go': return stream(go as StreamParser<unknown>)
    case '.sh': case '.bash': return stream(shell as StreamParser<unknown>)
    case '.ps1': case '.psm1': return stream(powerShell as StreamParser<unknown>)
    default: return []
  }
}

/** Token colours come from CSS variables so both themes are defined in styles.css. */
const highlight = HighlightStyle.define([
  { tag: [t.keyword, t.controlKeyword, t.definitionKeyword, t.moduleKeyword, t.operatorKeyword], color: 'var(--syn-keyword)' },
  { tag: [t.string, t.special(t.string), t.regexp], color: 'var(--syn-string)' },
  { tag: [t.comment, t.lineComment, t.blockComment, t.docComment], color: 'var(--syn-comment)', fontStyle: 'italic' },
  { tag: [t.number, t.bool, t.null, t.atom], color: 'var(--syn-number)' },
  { tag: [t.typeName, t.className, t.namespace, t.standard(t.variableName)], color: 'var(--syn-type)' },
  { tag: [t.function(t.variableName), t.function(t.propertyName), t.definition(t.variableName)], color: 'var(--syn-function)' },
  { tag: [t.meta, t.annotation, t.processingInstruction], color: 'var(--syn-meta)' },
  { tag: [t.heading], color: 'var(--syn-keyword)', fontWeight: '600' },
  { tag: [t.link, t.url], color: 'var(--syn-string)', textDecoration: 'underline' },
  { tag: t.emphasis, fontStyle: 'italic' },
  { tag: t.strong, fontWeight: '600' },
])

const base: Extension = [
  EditorState.readOnly.of(true),
  EditorView.editable.of(false),
  lineNumbers(),
  EditorView.lineWrapping,
  highlightSpecialChars(),
  syntaxHighlighting(highlight),
]

/** Reports the lines in view and the lines of selected text, so agents know which part is "this". */
function trackLines(view: EditorView): () => void {
  let frame = 0
  const update = () => {
    frame = 0
    readerLines.value = { visible: visibleLines(view), selection: selectedLines(view) }
  }
  const schedule = () => {
    if (!frame) frame = requestAnimationFrame(update)
  }
  const resize = new ResizeObserver(schedule)
  resize.observe(view.scrollDOM)
  view.scrollDOM.addEventListener('scroll', schedule, { passive: true })
  document.addEventListener('selectionchange', schedule)
  schedule()
  return () => {
    cancelAnimationFrame(frame)
    resize.disconnect()
    view.scrollDOM.removeEventListener('scroll', schedule)
    document.removeEventListener('selectionchange', schedule)
    readerLines.value = { visible: null, selection: null }
  }
}

function visibleLines(view: EditorView): [number, number] | null {
  const box = view.scrollDOM.getBoundingClientRect()
  if (box.height <= 0) return null
  const doc = view.state.doc
  const first = view.lineBlockAtHeight(box.top - view.documentTop)
  const last = view.lineBlockAtHeight(box.bottom - 1 - view.documentTop)
  return [doc.lineAt(first.from).number, doc.lineAt(last.to).number]
}

/** The editor is read-only, so text selection lives in the page, not in the editor state. */
function selectedLines(view: EditorView): [number, number] | null {
  const selection = document.getSelection()
  if (!selection || selection.isCollapsed || selection.rangeCount === 0) return null
  const span = selection.getRangeAt(0)
  if (!view.contentDOM.contains(span.startContainer) || !view.contentDOM.contains(span.endContainer)) return null
  try {
    const from = view.posAtDOM(span.startContainer, span.startOffset)
    const to = view.posAtDOM(span.endContainer, span.endOffset)
    const doc = view.state.doc
    return [doc.lineAt(Math.min(from, to)).number, doc.lineAt(Math.max(from, to)).number]
  } catch {
    return null
  }
}

type Content =
  | { state: 'loading' }
  | { state: 'error'; message: string }
  | { state: 'binary' }
  | { state: 'file'; text: string; truncated: boolean }
  | { state: 'diff'; before: string; after: string; truncated: boolean }

interface FileReply { text?: string; binary?: boolean; truncated?: boolean; error?: string }
interface DiffReply { before?: string | null; after?: string | null; binary?: boolean; truncated?: boolean; error?: string }

export function Reader({ model: m, target }: { model: Model; target: ReaderTarget }) {
  const host = useRef<HTMLDivElement>(null)
  const [content, setContent] = useState<Content>({ state: 'loading' })
  const node = target.node
  const commit = target.commit
  const diff = commit !== null
  const sha = typeof commit === 'number' ? m.commits[commit]?.sha : undefined

  // Load whenever the file, the compared commit or the file on disk changes.
  useEffect(() => {
    let live = true
    setContent({ state: 'loading' })
    const reply = diff
      ? request<DiffReply>({ type: 'readDiff', path: node.path, commit: commit === 'working' ? 'working' : sha ?? '' }).then((r): Content =>
          r.error ? { state: 'error', message: r.error }
          : r.binary ? { state: 'binary' }
          : { state: 'diff', before: r.before ?? '', after: r.after ?? '', truncated: !!r.truncated })
      : request<FileReply>({ type: 'readFile', path: node.path }).then((r): Content =>
          r.error ? { state: 'error', message: r.error }
          : r.binary ? { state: 'binary' }
          : { state: 'file', text: r.text ?? '', truncated: !!r.truncated })
    reply.then(c => live && setContent(c), (e: Error) => live && setContent({ state: 'error', message: e.message }))
    return () => { live = false }
  }, [node.path, commit, sha, node.src.modified, node.src.bytes])

  // One read-only editor per loaded content.
  useEffect(() => {
    if (!host.current || (content.state !== 'file' && content.state !== 'diff')) return
    const extensions: Extension[] = [base, languageFor(node.path)]
    if (content.state === 'diff') {
      extensions.push(unifiedMergeView({
        original: content.before,
        mergeControls: false,
        highlightChanges: true,
        gutter: true,
        collapseUnchanged: { margin: 3, minSize: 6 },
      }))
    }
    const view = new EditorView({
      parent: host.current,
      state: EditorState.create({ doc: content.state === 'diff' ? content.after : content.text, extensions }),
    })
    const stop = trackLines(view)
    return () => {
      stop()
      view.destroy()
    }
  }, [content])

  // Recent commits in the selected range that touched this file, newest first.
  const [from, to] = range.value
  const history: number[] = []
  for (let i = to - 1; i >= from && history.length < 8; i--) if (m.commits[i].files.includes(node.src.id!)) history.push(i)
  if (typeof commit === 'number' && !history.includes(commit)) history.unshift(commit)
  const show = (c: number | 'working' | null) => (reader.value = { node, commit: c })
  const counts = activity.value
  const src = node.src
  const modified = src.modified ? new Date(src.modified) : null

  return (
    <aside class="side reader" aria-label="Reader">
      <div class="side-head">
        <h2 class="side-title"><Icon name="fileLines" />{displayName(m, node)}</h2>
        <button class="icon-button" aria-label="Close reader" title="Close (Esc)" onClick={() => (reader.value = null)}>
          <Icon name="close" />
        </button>
      </div>
      <div class="side-path mono">{node.path}</div>
      <div class="price num">
        {src.lines !== undefined && <span aria-label={`${format.int(src.lines)} lines`}><Icon name="lines" />{format.int(src.lines)}</span>}
        {src.bytes !== undefined && <span>{format.bytes(node.bytes)}</span>}
        {m.scan.project.git && counts && <span><Icon name="commit" label="Commits in range" />{format.int(counts[node.index])}</span>}
        {modified && <span title={format.dateLong(modified)}><Icon name="clock" label="Modified" />{format.ago(modified)}</span>}
      </div>
      <div class="chips" role="group" aria-label="Show">
        <button class="chip" aria-pressed={commit === null} onClick={() => show(null)}><Icon name="fileLines" />File</button>
        {src.change && (
          <button class="chip change" aria-pressed={commit === 'working'} title="Uncommitted changes" onClick={() => show('working')}>
            <Icon name="change" />uncommitted
          </button>
        )}
        {history.map(i => (
          <button class="chip mono" aria-pressed={commit === i}
            title={`${m.commits[i].subject} · ${format.dateShort(format.fromUnix(m.commits[i].time))}`} onClick={() => show(i)}>
            {m.commits[i].sha.slice(0, 7)}
          </button>
        ))}
      </div>
      {content.state === 'loading' && <div class="note">…</div>}
      {content.state === 'error' && <div class="note">{content.message}</div>}
      {content.state === 'binary' && <div class="note">Binary content{node.src.bytes !== undefined ? ` · ${format.bytes(node.bytes)}` : ''}</div>}
      {(content.state === 'file' || content.state === 'diff') && content.truncated && <div class="note">Showing the first 1 MiB</div>}
      <div class="reader-code" ref={host} />
    </aside>
  )
}
