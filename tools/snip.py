import re, sys, os

path = sys.argv[1]
kws = sys.argv[2].split(',')
radius = int(sys.argv[3]) if len(sys.argv) > 3 else 220
txt = open(path, encoding='utf-8', errors='replace').read()
print('len', len(txt))
for kw in kws:
    for m in re.finditer(re.escape(kw), txt, re.IGNORECASE):
        s = max(0, m.start() - radius)
        e = min(len(txt), m.end() + radius)
        frag = txt[s:e].replace('\n', ' ')
        print(f'--- {kw} @{m.start()}\n{frag}\n')
