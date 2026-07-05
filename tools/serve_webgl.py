import http.server, socketserver

PORT = 8124
DIR = r"C:\Users\Junius\Desktop\unit_game\unity_game\Builds"

# Content types for Unity build payloads (keyed on the inner ext, sans .br/.gz)
TYPES = {
    ".js": "application/javascript",
    ".wasm": "application/wasm",
    ".data": "application/octet-stream",
    ".symbols.json": "application/octet-stream",
}

class H(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=DIR, **k)

    def guess_type(self, path):
        p = str(path)
        if p.endswith(".br") or p.endswith(".gz"):
            p = p[:-3]
        for ext, ct in TYPES.items():
            if p.endswith(ext):
                return ct
        return super().guess_type(p)  # .html, .css, .png, etc.

    def end_headers(self):
        path = self.path.split("?")[0]
        if path.endswith(".br"):
            self.send_header("Content-Encoding", "br")
        elif path.endswith(".gz"):
            self.send_header("Content-Encoding", "gzip")
        # Never let the browser cache (avoids stale entries from earlier broken runs)
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        self.send_header("Pragma", "no-cache")
        super().end_headers()

socketserver.TCPServer.allow_reuse_address = True
with socketserver.TCPServer(("", PORT), H) as httpd:
    print(f"Serving {DIR} at http://localhost:{PORT}/")
    httpd.serve_forever()
