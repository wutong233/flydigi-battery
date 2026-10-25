"""Decode Flydigi Space Station IPC replies with the full protobuf schema.

Builds a dynamic protobuf pool from tools/schema.json (decoded FileDescriptorProto),
talks to the service pipe, and pretty-prints whatever the service returns.

Usage:
    python fcs_probe.py 4097 4099 4100 4188
"""
import base64
import json
import os
import re
import sys
from google.protobuf import descriptor_pb2
from google.protobuf import descriptor_pool
from google.protobuf import json_format
from google.protobuf import message_factory

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fcs_client import Client  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
PKG = "Flydigi.SharedResources.data.protobuf"

TYPE_MAP = {
    "double": descriptor_pb2.FieldDescriptorProto.TYPE_DOUBLE,
    "float": descriptor_pb2.FieldDescriptorProto.TYPE_FLOAT,
    "int64": descriptor_pb2.FieldDescriptorProto.TYPE_INT64,
    "uint32": descriptor_pb2.FieldDescriptorProto.TYPE_UINT32,
    "int32": descriptor_pb2.FieldDescriptorProto.TYPE_INT32,
    "bool": descriptor_pb2.FieldDescriptorProto.TYPE_BOOL,
    "string": descriptor_pb2.FieldDescriptorProto.TYPE_STRING,
    "message": descriptor_pb2.FieldDescriptorProto.TYPE_MESSAGE,
    "enum": descriptor_pb2.FieldDescriptorProto.TYPE_ENUM,
    "bytes": descriptor_pb2.FieldDescriptorProto.TYPE_BYTES,
}


def load_files():
    with open(os.path.join(HERE, "schema.json"), encoding="utf-8") as fh:
        return [item["fd"] for item in json.load(fh)]


def _add_enum(parent, e):
    ee = parent.enum_type.add()
    ee.name = e["name"]
    for name, num in e["values"]:
        ev = ee.value.add()
        ev.name = name
        ev.number = num


def _add_message(parent, m, nested=False):
    mm = (parent.nested_type if nested else parent.message_type).add()
    mm.name = m["name"]
    for en in m.get("enums", []) or []:
        _add_enum(mm, en)
    for nm in m.get("nested", []) or []:
        _add_message(mm, nm, nested=True)
    for f in m["fields"]:
        ff = mm.field.add()
        ff.name = f["name"]
        ff.number = f["num"]
        ff.type = TYPE_MAP[f["type"]]
        ff.label = (descriptor_pb2.FieldDescriptorProto.LABEL_REPEATED
                    if f["label"] == "repeated"
                    else descriptor_pb2.FieldDescriptorProto.LABEL_OPTIONAL)
        if f.get("type_name"):
            ff.type_name = f["type_name"]


def build_pool():
    """从 app.asar 里取出原始的 FileDescriptorProto 字节，直接喂给 descriptor pool。"""
    pool = descriptor_pool.DescriptorPool()
    blobs = _descriptor_blobs()
    pending = list(blobs.items())
    while pending:
        progressed = False
        rest = []
        for name, raw in pending:
            try:
                pool.AddSerializedFile(raw)
                progressed = True
            except Exception:  # noqa: BLE001 — 依赖还没加进去，下一轮再试
                rest.append((name, raw))
        pending = rest
        if not progressed:
            for name, raw in pending:
                print("!! 无法加载描述符 %s" % name, file=sys.stderr)
            break
    return pool


def _descriptor_blobs():
    cache = os.path.join(HERE, "desc_bin")
    if os.path.isdir(cache):
        out = {}
        for fn in os.listdir(cache):
            if fn.endswith(".bin"):
                with open(os.path.join(cache, fn), "rb") as fh:
                    out[fn[:-4]] = fh.read()
        if out:
            return out
    asar = r"C:\Program Files\Flydigi Space Station\resources\app.asar"
    with open(asar, "rb") as fh:
        data = fh.read()
    out = {}
    for m in re.finditer(rb'"([A-Za-z0-9+/]{200,}={0,2})"', data):
        try:
            raw = base64.b64decode(m.group(1))
        except Exception:  # noqa: BLE001
            continue
        if raw[:1] != b"\x0a":
            continue
        try:  # 第一个字段 = 文件名
            i = 1
            ln, i = _read_varint(raw, i)
            name = raw[i:i + ln].decode()
        except Exception:  # noqa: BLE001
            continue
        if name.endswith(".proto"):
            out[name] = raw
    os.makedirs(cache, exist_ok=True)
    for name, raw in out.items():
        with open(os.path.join(cache, name + ".bin"), "wb") as fh:
            fh.write(raw)
    return out


def _read_varint(b, i):
    r = 0
    s = 0
    while True:
        x = b[i]
        i += 1
        r |= (x & 0x7F) << s
        if not x & 0x80:
            return r, i
        s += 7


def classes(pool):
    def get(name):
        return message_factory.GetMessageClass(pool.FindMessageTypeByName(PKG + "." + name))
    return get("IpcCommand"), get("IpcResult")


def enum_name(pool, num):
    try:
        ed = pool.FindEnumTypeByName(PKG + ".IpcCommandEnum")
        v = ed.values_by_number.get(num)
        return v.name if v else str(num)
    except Exception:  # noqa: BLE001
        return str(num)


def param_field_for(Cmd, cmd_id, pool):
    """cmdId -> IpcCommand field whose message type is the matching *Params."""
    try:
        ed = pool.FindEnumTypeByName(PKG + ".IpcCommandEnum")
        v = ed.values_by_number.get(cmd_id)
        if not v:
            return None, None
        stem = v.name.replace("IpcCommandEnum_", "")
    except Exception:  # noqa: BLE001
        return None, None
    for cand in (stem + "Params", stem + "Param", stem + "s"):
        try:
            md = pool.FindMessageTypeByName(PKG + "." + cand)
        except KeyError:
            continue
        for fld in Cmd.DESCRIPTOR.fields:
            if fld.message_type is not None and fld.message_type.full_name == md.full_name:
                return fld, cand
    return None, None


def call(cl, Cmd, R, pool, cmd_id, device=None, extra=None):
    msg = Cmd()
    msg.cmdId = cmd_id
    fld, cname = param_field_for(Cmd, cmd_id, pool)
    if fld is not None:
        param = getattr(msg, fld.name)
        want = dict(extra or {})
        if device:
            for k, v in device.items():
                if k in param.DESCRIPTOR.fields_by_name and not want.get(k):
                    want[k] = v
        for k, v in want.items():
            if k in param.DESCRIPTOR.fields_by_name:
                setattr(param, k, v)
    raw = cl.request(msg.SerializeToString())
    res = R()
    res.ParseFromString(raw)
    return res


def main():
    ids = [int(a) for a in sys.argv[1:]] or [4097]
    pool = build_pool()
    Cmd, R = classes(pool)
    cl = Client()
    cl.connect()
    try:
        device = None
        for cid in ids:
            out = {"cmdId": cid, "name": enum_name(pool, cid)}
            try:
                res = call(cl, Cmd, R, pool, cid, device=device)
                out["result"] = json_format.MessageToDict(res, preserving_proto_field_name=True)
                if device is None:
                    devs = (out["result"].get("getConnectedDevices") or {}).get("connectedDevices") or []
                    if devs:
                        device = {k: devs[0].get(k) for k in ("deviceCode", "uid") if devs[0].get(k)}
            except Exception as ex:  # noqa: BLE001
                out["error"] = repr(ex)
            print(json.dumps(out, ensure_ascii=False, indent=2))
    finally:
        cl.close()


if __name__ == "__main__":
    main()
