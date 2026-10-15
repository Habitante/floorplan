import regularFont from '@fortawesome/fontawesome-free/webfonts/fa-regular-400.woff2?url'
import solidFont from '@fortawesome/fontawesome-free/webfonts/fa-solid-900.woff2?url'
import { GLYPHS, iconFontsLoaded, type IconName } from './glyphs'

export function Icon({ name, label }: { name: IconName; label?: string }) {
  return label
    ? <span class="icon" role="img" aria-label={label} title={label}>{GLYPHS[name]}</span>
    : <span class="icon" aria-hidden="true">{GLYPHS[name]}</span>
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
