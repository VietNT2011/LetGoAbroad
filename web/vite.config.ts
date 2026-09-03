import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
// VI: API và cookie cùng origin qua proxy. EN: Proxy keeps browser API requests and cookies same-origin.
export default defineConfig({ plugins: [react()], server: { port: 5173, proxy: { '/api': 'http://localhost:5080', '/hubs': { target: 'http://localhost:5080', ws: true }, '/openapi': 'http://localhost:5080' } } });
