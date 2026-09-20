import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
// VI: API và cookie cùng origin qua proxy. EN: Proxy keeps browser API requests and cookies same-origin.
export default defineConfig({ plugins: [react()], server: { port: 5173, proxy: { '/api': 'http://127.0.0.1:5188', '/hubs': { target: 'http://127.0.0.1:5188', ws: true }, '/openapi': 'http://127.0.0.1:5188' } } });
