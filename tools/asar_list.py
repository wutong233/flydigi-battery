import json, os, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASAR = os.environ.get("FLYDIGI_ASAR", r"C:\Program Files\Flydigi Space Station\resources\app.asar")
OUT = os.path.join(ROOT, "asar_out")

f = open(ASAR, 'rb')
# asar: uint32 4, uint32 header_size(pickle), then header json
hdr = f.read(16)
(size1,) = struct.unpack('<I', hdr[0:4])
(size2,) = struct.unpack('<I', hdr[4:8])
(size3,) = struct.unpack('<I', hdr[8:12])
(strlen,) = struct.unpack('<I', hdr[12:16])
header_json = f.read(strlen).decode('utf-8')
base = 16 + strlen
# align to 4
base = (base + 3) & ~3
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
print('files:', len(files))
for p, off, sz in files[:40]:
    print(f'{sz:10d} {p}')
print('...')
print('total size', sum(s for _, _, s in files))
