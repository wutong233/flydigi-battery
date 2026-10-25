import re, sys

path = r"C:\Program Files\Flydigi Space Station\SpaceStationService.exe"
data = open(path, 'rb').read()
print('size', len(data))

def ascii_strings(buf, minlen=5):
    out = []
    for m in re.finditer(rb'[\x20-\x7e]{%d,}' % minlen, buf):
        out.append((m.start(), m.group().decode('ascii', 'replace')))
    return out

def utf16_strings(buf, minlen=4):
    out = []
    # UTF-16LE printable pairs: char then \x00
    for m in re.finditer((rb'(?:[\x20-\x7e]\x00){%d,}' % minlen), buf):
        try:
            s = m.group().decode('utf-16-le')
        except Exception:
            continue
        out.append((m.start(), s))
    return out

kw = ['fcs', 'Fcs', 'cmdId', 'Pipe', 'pipe', 'Battery', 'battery', 'Power', '电量', 'Socket', 'Json', 'json', 'Length', 'Frame']

a = ascii_strings(data)
u = utf16_strings(data)
print('ascii strings', len(a), 'utf16 strings', len(u))

for name, lst in (('ASCII', a), ('UTF16', u)):
    print('=====', name)
    seen = set()
    for off, s in lst:
        low = s.lower()
        if any(k.lower() in low for k in kw):
            if len(s) < 200 and s not in seen:
                seen.add(s)
                print(hex(off), repr(s))
