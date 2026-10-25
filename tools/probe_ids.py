"""Try a list of cmdIds on one pipe connection and report which ones answer."""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import fcs_client as C  # noqa: E402
import fcs_dump as D  # noqa: E402

DEFAULT = [4097, 4099, 4100, 4112, 4144, 4160, 4188, 28683, 28684, 28687, 4128, 4138]


def main():
    D.load_schema()
    ids = [int(x) for x in sys.argv[1:]] or DEFAULT
    cl = C.Client()
    cl.connect()
    dev_cfg = {"deviceCode": "k5", "uid": "0123456789abcdef0123456789"}
    for cid in ids:
        pfq, pnum = D.param_for(cid)
        name = (D.ENUMS.get(D.PKG + ".IpcCommandEnum") or {}).get(cid, "?")
        payload = C.f_varint(1, cid)
        if pfq:
            payload += C.f_bytes(pnum, D.encode_param(pfq, dev_cfg))
        try:
            res = cl.request(payload, 3.0)
        except Exception as ex:  # noqa: BLE001
            print("%-6d %-42s NO REPLY (%s)" % (cid, name, type(ex).__name__))
            continue
        obj = D.decode(res, D.PKG + ".IpcResult")
        state = obj.get("state", [None])[0]
        print("%-6d %-42s OK state=%s len=%d" % (cid, name, state, len(res)))
        D.show(obj, D.PKG + ".IpcResult")
        print("-" * 70)
    sys.stdout.flush()
    os._exit(0)


if __name__ == "__main__":
    main()
