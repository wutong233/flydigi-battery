import json, os, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASAR = os.environ.get("FLYDIGI_ASAR", r"C:\Program Files\Flydigi Space Station\resources\app.asar")
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "asar_full")

f = open(ASAR, "rb")
# --- asar header ---
size_buf = f.read(16)
(_, header_size, _, json_size) = struct.unpack("<4I", size_buf)
header = json.loads(f.read(json_size).decode("utf-8"))
base = 8 + header_size


def walk(node, path=""):
    for name, info in node.get("files", {}).items():
        p = os.path.join(path, name)
        if "files" in info:
            yield from walk(info, p)
        else:
            yield p, info


count = 0
for rel, info in walk(header):
    if "offset" not in info:
        continue
    dst = os.path.join(OUT, rel.replace("/", "__").replace("\\", "__"))
    if os.path.exists(dst) and os.path.getsize(dst) == info["size"]:
        continue
    os.makedirs(OUT, exist_ok=True)
    f.seek(base + int(info["offset"]))
    data = f.read(info["size"])
    with open(dst, "wb") as g:
        g.write(data)
    count += 1
print("extracted", count)
