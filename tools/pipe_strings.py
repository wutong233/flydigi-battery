import re
p=r"C:\Program Files\Flydigi Space Station\SpaceStationService.exe"
b=open(p,"rb").read()
print("size",len(b))
def asc(bs):
    return re.findall(rb"[\x20-\x7e]{4,}", bs)
def utf16(bs):
    return re.findall(rb"(?:[\x20-\x7e]\x00){4,}", bs)
hits=set()
for pat in (rb"[\x20-\x7e]{3,}",):
    for m in re.finditer(pat,b):
        s=m.group().decode("latin1")
        if "pipe" in s.lower() or "fcs" in s.lower():
            hits.add(s)
for m in re.finditer(rb"(?:[\x20-\x7e]\x00){3,}",b):
    s=m.group().decode("utf-16-le","replace")
    if "pipe" in s.lower() or "pipe" in s.lower() or "fcs" in s.lower():
        hits.add("U16:"+s)
for s in sorted(hits)[:80]:
    print(repr(s))
print("total", len(hits))
