import regularFont from '@fortawesome/fontawesome-free/webfonts/fa-regular-400.woff2?url'
import solidFont from '@fortawesome/fontawesome-free/webfonts/fa-solid-900.woff2?url'
import { GLYPHS, iconFontsLoaded, type IconName } from './glyphs'
import type { PriceTag } from './render'

export function Icon({ name, label }: { name: IconName; label?: string }) {
  return label
    ? <span class="icon" role="img" aria-label={label} title={label}>{GLYPHS[name]}</span>
    : <span class="icon" aria-hidden="true">{GLYPHS[name]}</span>
}

/** A price tag: line counts carry the lines glyph instead of a unit word. */
export function Tag({ tag }: { tag: PriceTag }) {
  return tag.lines
    ? <span class="tag" aria-label={`${tag.text} lines`}><Icon name="lines" />{tag.text}</span>
    : <span class="tag">{tag.text}</span>
}

export function loadIconFonts() {
  const faces = [
    new FontFace('map-icons', 'url(/user-fonts/icons)'),
    new FontFace('map-icons-free', `url(${regularFont})`),
    new FontFace('map-icons-free-solid', `url(${solidFont})`),
  ]
  for (const face of faces) {
    face.load().then(
      f => {
        document.fonts.add(f)
        iconFontsLoaded.value++
      },
      () => {},
    )
  }
}
