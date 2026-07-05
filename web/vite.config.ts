import { defineConfig } from "vite";
import fs from "node:fs";
import path from "node:path";

// Serve the Unity WebGL Brotli-compressed build files (.unityweb) with the right Content-Encoding +
// Content-Type so the browser/Unity loader can decode them on the dev server (no host config needed).
const unityWebgl = {
  name: "unity-webgl-serve",
  configureServer(server: any) {
    server.middlewares.use((req: any, res: any, next: any) => {
      const url = (req.url || "").split("?")[0];
      if (url.startsWith("/unity/") && url.endsWith(".unityweb")) {
        const file = path.join(process.cwd(), "public", url);
        if (fs.existsSync(file)) {
          res.setHeader("Content-Encoding", "br");
          res.setHeader(
            "Content-Type",
            url.includes(".wasm") ? "application/wasm" : url.includes(".js") ? "application/javascript" : "application/octet-stream"
          );
          res.setHeader("Cache-Control", "no-store");
          fs.createReadStream(file).pipe(res);
          return;
        }
      }
      next();
    });
  },
};

export default defineConfig({
  plugins: [unityWebgl],
  server: {
    port: 5173,
    // Fail loudly if 5173 is taken instead of silently spawning a zombie on 5174/5175.
    strictPort: true,
    open: true,
    headers: { "Cache-Control": "no-store" },
  },
});
