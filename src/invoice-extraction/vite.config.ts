import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    target: 'node24',
    ssr: 'src/index.ts',
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: true,
    minify: false,
    rollupOptions: {
      output: {
        entryFileNames: 'index.js',
      },
    },
  },
});
