import { javascript } from '@codemirror/lang-javascript'
import { markdown } from '@codemirror/lang-markdown'
import { HighlightStyle, StreamLanguage, syntaxHighlighting, unfoldAll, type StreamParser } from '@codemirror/language'
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
import { Compartment, EditorState, RangeSetBuilder, type Extension, type Text } from '@codemirror/state'
import { Decoration, EditorView, highlightSpecialChars, lineNumbers } from '@codemirror/view'
import { tags as t } from '@lezer/highlight'
import { signal } from '@preact/signals'
import { useEffect, useRef, useState } from 'preact/hooks'
import { request } from './bridge'
import * as format from './format'
import { Icon } from './icons'
import { displayName, type Model } from './model'
import { pointedLines } from './board'
import { Mentions } from './mentions'
import { RelatedFiles } from './related'
import { changeMarks, foldSkeleton, lineChanges, outlineFolding, pointedLines as pointedFacet, revealLines, type FileOutline, type LineChange } from './skeleton'
import { activity, NO_LINES, pointers, range, reader, readerFocus, readerLines, type ReaderTarget } from './state'
import type { WorkingChange } from './types'

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

const SELECTED_TEXT_LIMIT = 4000

/** Reports the lines in view and the selected text, so agents know which part is "this". */
function trackLines(view: EditorView): () => void {
  let frame = 0
  const update = () => {
    frame = 0
    const chosen = selectedText(view)
    readerLines.value = {
      visible: visibleLines(view),
      selection: chosen?.lines ?? null,
      text: chosen?.text ?? null,
      removed: chosen?.removed ?? false,
    }
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
    readerLines.value = NO_LINES
  }
}

/** The file's last line: a final newline leaves an empty editor line that wc and Git do not count. */
function lastLine(doc: Text): number {
  return doc.lines > 1 && doc.line(doc.lines).length === 0 ? doc.lines - 1 : doc.lines
}

function lineNumber(doc: Text, position: number): number {
  return Math.min(doc.lineAt(position).number, lastLine(doc))
}

function visibleLines(view: EditorView): [number, number] | null {
  const box = view.scrollDOM.getBoundingClientRect()
  if (box.height <= 0) return null
  const doc = view.state.doc
  const first = view.lineBlockAtHeight(box.top - view.documentTop)
  const last = view.lineBlockAtHeight(box.bottom - 1 - view.documentTop)
  return [lineNumber(doc, first.from), lineNumber(doc, last.to)]
}

/**
 * The selected lines and text. The editor is read-only, so the selection lives in the page, not in
 * the editor state; in a diff it may include removed lines, which have no number of their own.
 */
function selectedText(view: EditorView): { lines: [number, number]; text: string; removed: boolean } | null {
  const selection = document.getSelection()
  if (!selection || selection.isCollapsed || selection.rangeCount === 0) return null
  const span = selection.getRangeAt(0)
  if (!view.contentDOM.contains(span.startContainer) || !view.contentDOM.contains(span.endContainer)) return null
  try {
    const from = view.posAtDOM(span.startContainer, span.startOffset)
    const to = view.posAtDOM(span.endContainer, span.endOffset)
    const doc = view.state.doc
    const removed = [...view.contentDOM.querySelectorAll('.cm-deletedChunk')].some(chunk => span.intersectsNode(chunk))
    return {
      lines: [lineNumber(doc, Math.min(from, to)), lineNumber(doc, Math.max(from, to))],
      text: selection.toString().slice(0, SELECTED_TEXT_LIMIT),
      removed,
    }
  } catch {
    return null
  }
}

const pointedLine = Decoration.line({ class: 'cm-pointed' })

/** Marks the lines agents point at. */
function lineHighlights(ranges: readonly (readonly [number, number])[]): Extension {
  return EditorView.decorations.of(view => {
    const doc = view.state.doc
    const lines = new Set<number>()
    for (const [from, to] of ranges) for (let n = Math.max(1, from); n <= Math.min(to, lastLine(doc)); n++) lines.add(n)
    const builder = new RangeSetBuilder<Decoration>()
    for (const n of [...lines].sort((a, b) => a - b)) builder.add(doc.line(n).from, doc.line(n).from, pointedLine)
    return builder.finish()
  })
}

/** Agents' lines: highlighted, kept out of folds and marked on the overview strip. */
const pointed = (ranges: readonly (readonly [number, number])[]): Extension => [lineHighlights(ranges), pointedFacet.of(ranges)]

type Content =
  | { state: 'loading' }
  | { state: 'error'; message: string }
  | { state: 'binary' }
  | { state: 'file'; text: string; truncated: boolean; outline: FileOutline | null; changes: LineChange[] }
  | { state: 'diff'; before: string; after: string; truncated: boolean }

interface FileReply { text?: string; binary?: boolean; truncated?: boolean; error?: string }
interface DiffReply { before?: string | null; after?: string | null; binary?: boolean; truncated?: boolean; error?: string }
interface OutlineReply { outline?: FileOutline | null; error?: string }

/** Files open as skeletons until the human asks for the whole file; the choice holds for the session. */
const skeletonView = signal(true)

/** A file with its outline and what changed since the last commit: a modified file is read with its committed text. */
async function loadFile(path: string, change: WorkingChange | undefined): Promise<Content> {
  const outline = request<OutlineReply>({ type: 'outline', path }).then(r => r.outline ?? null, () => null)
  if (change === 'modified') {
    const [r, o] = await Promise.all([request<DiffReply>({ type: 'readDiff', path, commit: 'working' }), outline])
    if (!r.error) {
      if (r.binary) return { state: 'binary' }
      const text = r.after ?? ''
      return { state: 'file', text, truncated: !!r.truncated, outline: o, changes: r.truncated ? [] : lineChanges(r.before ?? '', text) }
    }
  }
  const [r, o] = await Promise.all([request<FileReply>({ type: 'readFile', path }), outline])
  if (r.error) return { state: 'error', message: r.error }
  if (r.binary) return { state: 'binary' }
  const text = r.text ?? ''
  // New at this path, a rename included: every line is new.
  const fresh = change === 'untracked' || change === 'added' || change === 'renamed'
  return { state: 'file', text, truncated: !!r.truncated, outline: o, changes: fresh && !r.truncated ? lineChanges('', text) : [] }
}

export function Reader({ model: m, target }: { model: Model; target: ReaderTarget }) {
  const host = useRef<HTMLDivElement>(null)
  const [content, setContent] = useState<Content>({ state: 'loading' })
  const node = target.node
  const commit = target.commit
  const diff = commit !== null
  const sha = typeof commit === 'number' ? m.commits[commit]?.sha : undefined
  const view = useRef<EditorView | null>(null)
  /** The file the editor shows: switching files, the old one stays until the new one loads. */
  const shown = useRef<string | null>(null)
  const highlights = useRef(new Compartment())
  // Agents' line numbers count in the working file, which a commit's diff does not show.
  const workingFile = commit === null || commit === 'working'
  const ranges = workingFile ? pointedLines(pointers.value, node.path) : []
  const rangesKey = JSON.stringify(ranges)
  const focusLines = readerFocus.value

  // Load whenever the file, the compared commit or the file on disk changes.
  useEffect(() => {
    let live = true
    setContent({ state: 'loading' })
    const reply = diff
      ? request<DiffReply>({ type: 'readDiff', path: node.path, commit: commit === 'working' ? 'working' : sha ?? '' }).then((r): Content =>
          r.error ? { state: 'error', message: r.error }
          : r.binary ? { state: 'binary' }
          : { state: 'diff', before: r.before ?? '', after: r.after ?? '', truncated: !!r.truncated })
      : loadFile(node.path, node.src.change)
    reply.then(c => live && setContent(c), (e: Error) => live && setContent({ state: 'error', message: e.message }))
    return () => { live = false }
  }, [node.path, commit, sha, node.src.modified, node.src.bytes, node.src.change])

  // One read-only editor per loaded content.
  useEffect(() => {
    if (!host.current || (content.state !== 'file' && content.state !== 'diff')) return
    const extensions: Extension[] = [base, languageFor(node.path), highlights.current.of(pointed(ranges))]
    if (content.state === 'file') {
      extensions.push(changeMarks(content.changes))
      if (content.outline?.folds.length) extensions.push(outlineFolding(content.outline, content.changes))
    } else {
      extensions.push(unifiedMergeView({
        original: content.before,
        mergeControls: false,
        highlightChanges: true,
        gutter: true,
        collapseUnchanged: { margin: 3, minSize: 6 },
      }))
    }
    const editor = new EditorView({
      parent: host.current,
      state: EditorState.create({ doc: content.state === 'diff' ? content.after : content.text, extensions }),
    })
    view.current = editor
    shown.current = node.path
    if (content.state === 'file' && content.outline && skeletonView.value) {
      const focus = readerFocus.value
      foldSkeleton(editor, content.outline, focus?.path === node.path ? [...ranges, [focus.from, focus.to] as const] : ranges)
    }
    const stop = trackLines(editor)
    return () => {
      stop()
      view.current = null
      shown.current = null
      editor.destroy()
    }
  }, [content])

  useEffect(() => {
    const editor = view.current
    if (!editor) return
    editor.dispatch({ effects: highlights.current.reconfigure(pointed(ranges)) })
    revealLines(editor, ranges)
  }, [rangesKey])

  // Bring an agent's lines into view once the file is shown.
  useEffect(() => {
    const editor = view.current
    if (!editor || !focusLines || focusLines.path !== node.path || shown.current !== node.path || !workingFile) return
    revealLines(editor, [[focusLines.from, focusLines.to]])
    const doc = editor.state.doc
    const line = doc.line(Math.min(Math.max(1, focusLines.from), lastLine(doc)))
    editor.dispatch({ effects: EditorView.scrollIntoView(line.from, { y: 'start', yMargin: 48 }) })
    readerFocus.value = null
  }, [content, focusLines])

  // Recent commits in the selected range that touched this file, newest first.
  const [from, to] = range.value
  const history: number[] = []
  for (let i = to - 1; i >= from && history.length < 8; i--) if (m.commits[i].files.includes(node.src.id!)) history.push(i)
  if (typeof commit === 'number' && !history.includes(commit)) history.unshift(commit)
  const show = (c: number | 'working' | null) => (reader.value = { node, commit: c })
  const counts = activity.value
  const src = node.src
  const modified = src.modified ? new Date(src.modified) : null
  const outline = content.state === 'file' && content.outline?.folds.length ? content.outline : null
  const toggleSkeleton = () => {
    skeletonView.value = !skeletonView.value
    const editor = view.current
    if (!editor || !outline) return
    if (skeletonView.value) foldSkeleton(editor, outline, ranges)
    else unfoldAll(editor)
  }

  return (
    <aside class="side reader" aria-label="Reader">
      <div class="side-head">
        <h2 class="side-title"><Icon name="fileLines" />{displayName(m, node)}</h2>
        {outline && (
          <button class="icon-button" aria-label={skeletonView.value ? 'Show the whole file' : 'Fold to the skeleton'}
            title={skeletonView.value ? 'Show the whole file' : 'Fold to the skeleton'} onClick={toggleSkeleton}>
            <Icon name={skeletonView.value ? 'unfold' : 'fold'} />
          </button>
        )}
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
      <RelatedFiles model={m} node={node} compact />
      <Mentions model={m} node={node} />
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
