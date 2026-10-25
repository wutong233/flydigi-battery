"""Send commands one by one and dump every frame the service pushes back."""
import os
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import fcs_client as C  # noqa: E402
import fcs_dump as D  # noqa: E402

frames = []
lock = threading.Lock()
first = threading.Event()


def reader(cl):
    while True:
        try:
            fr = cl.read_frame()
        except Exception:  # noqa: BLE001
            return
        with lock:
            frames.append(fr)
        first.set()


def main():
    D.load_schema()
    args = [a for a in sys.argv[1:] if a.isdigit()]
    ids = [int(a) for a in args] or [4099]
    wait = float(os.environ.get("WAIT", "2.5"))
    cl = C.Client()
    cl.connect()
    if os.environ.get("DO_INIT") == "1":
        print("init: %d bytes" % len(D.do_init(cl) or b""))
    _, dev = C.get_connected_devices(cl)
    code, uid = "k5", "0123456789abcdef0123456789"
    if dev:
        code, uid = dev[0]["deviceCode"], dev[0]["uid"]

    th = threading.Thread(target=reader, args=(cl,), daemon=True)
    th.start()

    for cid in ids:
        with lock:
            frames.clear()
        first.clear()
        pfq, pnum = D.param_for(cid)
        payload = C.f_varint(1, cid)
        if cid not in (1, 4097):
            payload += C.f_varint(2, 1)
        if pfq:
            payload += C.f_bytes(pnum, D.encode_param(pfq, {"deviceCode": code, "uid": uid}))
        cl.send_raw(payload)
        first.wait(wait)
        time.sleep(wait)
        with lock:
            got = list(frames)
        print("=" * 60)
        print("cmd %d (%s) -> %d frame(s)" % (
            cid, (D.ENUMS.get(D.PKG + ".IpcCommandEnum") or {}).get(cid, "?"), len(got)))
        for i, fr in enumerate(got):
            print("  -- frame %d (%d bytes)" % (i, len(fr)))
            D.show(D.decode(fr, D.PKG + ".IpcResult"), D.PKG + ".IpcResult")
    sys.stdout.flush()
    os._exit(0)


if __name__ == "__main__":
    main()
