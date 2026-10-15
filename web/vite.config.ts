/// <reference types="vitest/config" />
import { createReadStream, existsSync } from 'node:fs'
import { join } from 'node:path'
import { defineConfig, type Plugin } from 'vite'

const here = import.meta.dirname

function iconFont(): string | null {
  const folder = join(process.env.LOCALAPPDATA ?? '', 'FloorPlan', 'fonts')
  for (const name of ['icons.woff2', 'icons.otf', 'icons.ttf']) {
    if (existsSync(join(folder, name))) return join(folder, name)
  }
  return null
}

/** Development only: stands in for the desktop host with a saved scan and the local icon font. */
function devHost(): Plugin {
  return {
    name: 'floorplan-dev-host',
    apply: 'serve',
    configureServer(server) {
      const serve = (route: string, file: () => string | null, type: string) =>
        server.middlewares.use(route, (_request, response) => {
          const path = file()
          if (!path || !existsSync(path)) {
            response.statusCode = 404
            response.end()
            return
          }
          response.setHeader('Content-Type', type)
          createReadStream(path).pipe(response)
        })
      serve('/__dev/scan.json', () => join(here, '.dev', 'scan.json'), 'application/json')
      serve('/user-fonts/icons', iconFont, 'font/otf')
    },
  }
}

export default defineConfig({
  base: './',
  plugins: [devHost()],
  oxc: { jsx: { runtime: 'automatic', importSource: 'preact' } },
  build: { outDir: 'dist', emptyOutDir: true, target: 'es2022', assetsInlineLimit: 0 },
  test: { include: ['src/**/*.test.ts'] },
})
