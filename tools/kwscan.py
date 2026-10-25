import os, re, sys

def scan_file(path, kws, ctx=110, max_hits=40, encodings=("utf-8",)):
    hits = []
    try:
        raw = open(path, "rb").read()
    except Exception as e:
        return hits
    for enc in encodings:
        try:
            text = raw.decode(enc, "ignore")
        except Exception:
            continue
        for kw in kws:
            for m in re.finditer(re.escape(kw), text, re.I):
                s = max(0, m.start() - ctx)
                e = min(len(text), m.end() + ctx)
                frag = text[s:e].replace("\r", " ").replace("\n", " ")
                hits.append((path, enc, kw, frag))
                if len(hits) >= max_hits:
                    return hits
    return hits

def main():
    root = sys.argv[1]
    kws = sys.argv[2].split(",")
    ctx = int(sys.argv[3]) if len(sys.argv) > 3 else 110
    maxhits = int(sys.argv[4]) if len(sys.argv) > 4 else 40
    exts = sys.argv[5].split(",") if len(sys.argv) > 5 else None
    n = 0
    for dirpath, dirnames, filenames in os.walk(root):
        for fn in filenames:
            if exts and not any(fn.endswith(x) for x in exts):
                continue
            p = os.path.join(dirpath, fn)
            for (path, enc, kw, frag) in scan_file(p, kws, ctx, maxhits - n):
                print(f"--- {os.path.basename(path)} [{enc}] kw={kw}")
                print("   " + frag)
                n += 1
                if n >= maxhits:
                    return

main()
