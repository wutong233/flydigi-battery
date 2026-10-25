"""Generic Flydigi IPC dumper.

Decodes any service reply with the schema extracted from app.asar
(tools/schema.json) using a pure protobuf wire decoder -- no descriptor
pool needed, so partially-known schema is fine.

Usage:
    python fcs_dump.py 4097                # GetConnectedDevices
    python fcs_dump.py 4099                # GetDeviceDetailInfo (auto device)
    python fcs_dump.py 4188 --param hex=D8C0
    python fcs_dump.py --raw 4097          # also dump unknown/raw bytes
"""
import json
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import fcs_client as C  # noqa: E402

PKG = "Flydigi.SharedResources.data.protobuf"
SCHEMA = os.path.join(HERE, "schema.json")

MSGS = {}   # fq name -> {"fields": {num: field}, "by_name": {...}}
ENUMS = {}  # fq name -> {num: name}


def load_schema():
    with open(SCHEMA, encoding="utf-8") as fh:
        files = json.load(fh)
    for item in files:
        fd = item["fd"]
        for e in fd.get("enums") or []:
            _add_enum(PKG + "." + e["name"], e)
        for m in fd.get("messages") or []:
            _add_msg(PKG + "." + m["name"], m)


def _add_enum(fq, e):
    ENUMS[fq] = {num: name for name, num in e.get("values") or []}


def _add_msg(fq, m):
    fields = {}
    for f in m.get("fields") or []:
        tn = f.get("type_name")
        if tn:
            tn = tn[1:] if tn.startswith(".") else tn
        fields[f["num"]] = {
            "name": f["name"],
            "type": f["type"],
            "type_name": tn,
            "label": f["label"],
        }
    MSGS[fq] = {
        "fields": fields,
        "by_name": {v["name"]: v for v in fields.values()},
    }
    for nm in m.get("nested") or []:
        _add_msg(fq + "." + nm["name"], nm)
    for e in m.get("enums") or []:
        _add_enum(fq + "." + e["name"], e)


def decode(buf, fq, depth=0):
    """Return a list of decoded values for message type fq."""
    info = MSGS.get(fq) or {}
    fmap = info.get("fields") or {}
    out = {}
    for fn, wt, v in C.parse(buf):
        fdef = fmap.get(fn)
        if not fdef:
            out["#%d" % fn] = v.hex() if isinstance(v, bytes) else v
            continue
        name = fdef["name"]
        out.setdefault(name, []).append(_value(v, wt, fdef, depth))
    return out


def _value(v, wt, fdef, depth):
    t = fdef["type"]
    tn = fdef["type_name"]
    if t in ("message",) and isinstance(v, bytes) and tn:
        if tn in MSGS:
            return decode(v, tn, depth + 1)
        return {"_raw": v.decode("latin-1")}
    if t == "enum":
        if isinstance(v, int):
            names = ENUMS.get(tn or "", {})
            return names.get(v, v)
        return v
    if t == "string":
        return v.decode("utf-8", "replace")
    if t == "bytes":
        return v.hex()
    if t in ("float", "double"):
        return struct.unpack("<f" if t == "float" else "<d", v)[0]
    return v


def show(obj, fq, indent=0):
    pad = "  " * indent
    info = MSGS.get(fq) or {}
    fields = info.get("fields") or {}
    order = {f["name"]: n for n, f in fields.items()}
    for key in sorted(obj, key=lambda k: order.get(k, 999)):
        vals = obj[key]
        fdef = fields.get(order.get(key)) or {}
        tn = fdef.get("type_name")
        for v in vals:
            if isinstance(v, dict) and tn in MSGS:
                print("%s%s:" % (pad, key))
                show(v, tn, indent + 1)
            else:
                print("%s%s: %s" % (pad, key, v))


def encode_param(fq, values):
    info = MSGS.get(fq) or {}
    bmap = info.get("by_name") or {}
    out = b""
    for k, v in values.items():
        f = bmap.get(k)
        if not f:
            continue
        num = None
        for n, ff in info["fields"].items():
            if ff is f:
                num = n
                break
        if f["type"] in ("string",):
            out += C.f_bytes(num, str(v).encode())
        else:
            out += C.f_varint(num, int(v))
    return out


def param_for(cmd_id):
    """Find (param fq name, IpcCommand field number) for a cmdId."""
    cmd_msg = MSGS.get(PKG + ".IpcCommand") or {}
    enum = ENUMS.get(PKG + ".IpcCommandEnum") or {}
    stem = None
    for num, name in enum.items():
        if num == cmd_id:
            stem = name.replace("IpcCommandEnum_", "")
            break
    if not stem:
        return None, None
    for cand in (stem + "Param", stem + "Params"):
        fq = PKG + "." + cand
        if fq in MSGS:
            for n, f in (cmd_msg.get("fields") or {}).items():
                if f.get("type_name") == fq:
                    return fq, n
    return None, None


def call(cl, cmd_id, values=None, category=None):
    values = values or {}
    pfq, pnum = param_for(cmd_id)
    payload = C.f_varint(1, cmd_id)
    if category is not None:
        payload += C.f_varint(2, category)
    if pfq:
        pb = encode_param(pfq, values)
        payload += C.f_bytes(pnum, pb)
    return cl.request(payload)


def do_init(cl):
    """服务端要先 Init 握手，后续按 category 路由的请求才会被处理。"""
    pfq, pnum = param_for(1)
    if not pfq:
        return None
    return cl.request(C.f_varint(1, 1) + C.f_bytes(pnum, encode_param(pfq, {"appVersion": "2.3.0", "language": "zh-CN"})), 3.0)


def main():
    load_schema()
    args = [a for a in sys.argv[1:]]
    raw_mode = "--raw" in args
    args = [a for a in args if a != "--raw"]
    category = 1
    params = {}
    ids = []
    for a in args:
        if a.startswith("--cat="):
            category = int(a.split("=", 1)[1])
            continue
        if "=" in a:
            k, v = a.split("=", 1)
            params[k] = v
        else:
            ids.append(int(a))
    ids = ids or [4097]

    cl = C.Client()
    cl.connect()
    try:
        do_init(cl)
        if not params.get("uid"):
            dev = C.get_connected_devices(cl)
            if dev:
                params.setdefault("deviceCode", dev[0].get("deviceCode"))
                params.setdefault("uid", dev[0].get("uid"))
        for cid in ids:
            print("=" * 60)
            print("cmdId %d  (%s)" % (cid, (ENUMS.get(PKG + ".IpcCommandEnum") or {}).get(cid, "?")))
            if cid in (1, 4097):
                cat = None
            else:
                cat = category
            try:
                res = call(cl, cid, params, cat)
            except Exception as ex:  # noqa: BLE001
                print("  !! %r" % (ex,))
                continue
            obj = decode(res, PKG + ".IpcResult")
            show(obj, PKG + ".IpcResult")
            if raw_mode:
                print("  raw: " + res.hex())
    finally:
        sys.stdout.flush()
        os._exit(0)


if __name__ == "__main__":
    main()
