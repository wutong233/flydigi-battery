import base64, os, re, sys, json

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)

# --- minimal protobuf wire reader -------------------------------------------------
def read_varint(b, i):
    r = 0; s = 0
    while True:
        x = b[i]; i += 1
        r |= (x & 0x7F) << s
        if not (x & 0x80):
            return r, i
        s += 7

def fields(b):
    i = 0; out = []
    while i < len(b):
        key, i = read_varint(b, i)
        fn = key >> 3; wt = key & 7
        if wt == 0:
            v, i = read_varint(b, i)
        elif wt == 1:
            v = b[i:i+8]; i += 8
        elif wt == 2:
            ln, i = read_varint(b, i); v = b[i:i+ln]; i += ln
        elif wt == 5:
            v = b[i:i+4]; i += 4
        else:
            raise ValueError('wt %d' % wt)
        out.append((fn, wt, v))
    return out

def s(b):
    return b.decode('utf-8', 'replace')

TYPES = {1:'double',2:'float',3:'int64',4:'uint64',5:'int32',6:'fixed64',7:'fixed32',8:'bool',
         9:'string',10:'group',11:'message',12:'bytes',13:'uint32',14:'enum',15:'sfixed32',
         16:'sfixed64',17:'sint32',18:'sint64'}
LABELS = {1:'optional',2:'required',3:'repeated'}

def parse_field(b):
    f = {'label':None,'type':None,'name':None,'num':None,'type_name':None}
    for fn, wt, v in fields(b):
        if fn == 1: f['name'] = s(v)
        elif fn == 3: f['num'] = v
        elif fn == 4: f['label'] = LABELS.get(v, v)
        elif fn == 5: f['type'] = TYPES.get(v, v)
        elif fn == 6: f['type_name'] = s(v)
    return f

def parse_enum(b):
    e = {'name':None,'values':[]}
    for fn, wt, v in fields(b):
        if fn == 1: e['name'] = s(v)
        elif fn == 2:
            vn = None; vnum = None
            for f2, w2, v2 in fields(v):
                if f2 == 1: vn = s(v2)
                elif f2 == 2: vnum = v2
            e['values'].append((vn, vnum))
    return e

def parse_msg(b):
    m = {'name':None,'fields':[],'nested':[],'enums':[],'oneofs':[]}
    for fn, wt, v in fields(b):
        if fn == 1: m['name'] = s(v)
        elif fn == 2: m['fields'].append(parse_field(v))
        elif fn == 3: m['nested'].append(parse_msg(v))
        elif fn == 4: m['enums'].append(parse_enum(v))
        elif fn == 8:
            for f2, w2, v2 in fields(v):
                if f2 == 1: m['oneofs'].append(s(v2))
    return m

def parse_file(b):
    fd = {'name':None,'package':None,'messages':[],'enums':[],'syntax':None}
    for fn, wt, v in fields(b):
        if fn == 1: fd['name'] = s(v)
        elif fn == 2: fd['package'] = s(v)
        elif fn == 4: fd['messages'].append(parse_msg(v))
        elif fn == 5: fd['enums'].append(parse_enum(v))
        elif fn == 12: fd['syntax'] = s(v)
    return fd

main = os.path.join(ROOT, "asar_out", ".vite__build__main.js")
t = open(main, encoding='utf-8', errors='replace').read()
blobs = [(m.start(), m.group(1)) for m in re.finditer(r'"([A-Za-z0-9+/]{200,}={0,2})"', t)]

out = []
for off, b64 in blobs:
    try:
        raw = base64.b64decode(b64)
    except Exception as ex:
        continue
    if not raw[:1] == b'\x0a':
        continue
    try:
        fd = parse_file(raw)
    except Exception as ex:
        continue
    if not fd['name'] or not fd['messages']:
        continue
    out.append((off, fd))

print('descriptor files found:', len(out))
for off, fd in out:
    print('---', off, fd['name'], fd['package'], fd['syntax'], 'msgs:', len(fd['messages']))
    for m in fd['messages']:
        print('   msg', m['name'], 'fields', len(m['fields']), 'nested', len(m['nested']))

json.dump([{'off': o, 'fd': fd} for o, fd in out],
          open(os.path.join(HERE, "schema.json"), 'w', encoding='utf-8'),
          ensure_ascii=False, indent=1)
print('written schema.json')
