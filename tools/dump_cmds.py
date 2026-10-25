import io, os, re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
src = os.path.join(ROOT, "asar_full", ".vite__renderer__main_window__assets__index-CxBxeAMi.js")
out = os.path.join(HERE, "out_cmds.txt")

text = open(src, "rb").read().decode("utf-8", "replace")

objs = []
for m in re.finditer(r"(\w+)=\{(cmdId|subCmdId):", text):
    name = m.group(1)
    start = text.index("{", m.start())
    depth = 0
    i = start
    in_str = None
    while i < len(text):
        c = text[i]
        if in_str:
            if c == "\\":
                i += 2
                continue
            if c == in_str:
                in_str = None
        elif c in "\"'`":
            in_str = c
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                break
        i += 1
    objs.append((name, text[start:i + 1]))

pretty = []
for name, body in objs:
    b = body.replace("},{", "},\n  {").replace("],", "],\n ")
    pretty.append("%s = %s" % (name, b))

with io.open(out, "w", encoding="utf-8") as f:
    f.write("count: %d\n\n" % len(objs))
    f.write("\n\n".join(pretty))
print("objs:", len(objs), "->", out)
