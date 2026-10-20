import { signal } from '@preact/signals'

// Font Awesome code points. A locally installed licensed font (served by the host as
// /user-fonts/icons) takes precedence; the free fonts are the distributable fallback.
export const GLYPHS = {
  folder: '',
  folderOpen: '',
  file: '',
  fileLines: '',
  lines: '',
  info: '',
  commit: '',
  branch: '',
  rotate: '',
  close: '',
  clock: '',
  change: '',
  pointer: '',
  locate: '',
  search: '',
  related: '',
  test: '',
  since: '',
  fold: '',
  unfold: '',
  folded: '',
  unfolded: '',
} as const

export type IconName = keyof typeof GLYPHS

export const ICON_FONT = '"map-icons", "map-icons-free", "map-icons-free-solid"'

/** Bumped as each icon font finishes loading, so canvases can redraw their glyphs. */
export const iconFontsLoaded = signal(0)
