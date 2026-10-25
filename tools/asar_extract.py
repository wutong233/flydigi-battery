import json, os, struct, re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASAR = os.environ.get("FLYDIGI_ASAR", r"C:\Program Files\Flydigi Space Station\resources\app.asar")
OUT = os.path.join(ROOT, "asar_out")
os.makedirs(OUT, exist_ok=True)

f = open(ASAR, 'rb')
hdr = f.read(16)
strlen = struct.unpack('<I', hdr[12:16])[0]
header_json = f.read(strlen).decode('utf-8')
base = (16 + strlen + 3) & ~3
header = json.loads(header_json)

files = []
def walk(node, path):
    for name, v in node.get('files', {}).items():
        p = path + '/' + name
        if 'files' in v:
            walk(v, p)
        else:
            files.append((p, int(v.get('offset', 0)), int(v.get('size', 0))))
walk(header, '')

exts = ('.js', '.mjs', '.cjs', '.json', '.html')
matched = []
for p, off, sz in files:
    if p.lower().endswith(exts) and sz < 20_000_000:
        f.seek(base + off)
        blob = f.read(sz)
        try:
            txt = blob.decode('utf-8', 'replace')
        except Exception:
            continue
        hits = []
        for kw in ('fcs.sock', 'cmdId', 'Battery', 'battery', '0x1003', '4099'):
            if kw in txt:
                hits.append(kw)
        if hits:
            matched.append((p, sz, hits))
            safe = p.strip('/').replace('/', '__')
            open(os.path.join(OUT, safe), 'wb').write(blob)

for p, sz, hits in matched:
    print(f'{sz:10d} {p}  {hits}')
print('matched', len(matched))
