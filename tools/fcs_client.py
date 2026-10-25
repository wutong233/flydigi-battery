"""Minimal client for the Flydigi Space Station service IPC pipe (fcs.sock).

Protocol (reverse engineered from the app's main.js):
  frame = b"FDG_PROTOCOL\n" + int32le(len) + protobuf(IpcCommand)
  response = same framing, protobuf(IpcResult)
"""
import os
import struct
import threading
import time

PIPE = r"\\.\pipe\fcs.sock"

# ---------------- protobuf helpers ----------------
def varint(n):
    if n < 0:
        n += 1 << 64
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        if n:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)

def tag(field, wt):
    return varint((field << 3) | wt)

def f_varint(field, value):
    return tag(field, 0) + varint(value)

def f_bytes(field, data):
    if isinstance(data, str):
        data = data.encode()
    return tag(field, 2) + varint(len(data)) + data

def read_varint(buf, i):
    r = 0
    s = 0
    while True:
        x = buf[i]
        i += 1
        r |= (x & 0x7F) << s
        if not x & 0x80:
            return r, i
        s += 7

def parse(buf):
    """Yield (field_number, wire_type, value)."""
    i = 0
    while i < len(buf):
        key, i = read_varint(buf, i)
        fn, wt = key >> 3, key & 7
        if wt == 0:
            v, i = read_varint(buf, i)
        elif wt == 2:
            ln, i = read_varint(buf, i)
            v = buf[i:i + ln]
            i += ln
        elif wt == 5:
            v = buf[i:i + 4]
            i += 4
        elif wt == 1:
            v = buf[i:i + 8]
            i += 8
        else:
            raise ValueError("wire type %d" % wt)
        yield fn, wt, v

def as_dict(buf):
    out = {}
    for fn, wt, v in parse(buf):
        if wt == 2:
            try:
                v.decode("utf-8")
                out.setdefault(fn, []).append(v)
            except UnicodeDecodeError:
                out.setdefault(fn, []).append(v)
        else:
            out.setdefault(fn, []).append(v)
    return out

# ---------------- pipe client ----------------
class Client:
    def __init__(self, path=PIPE):
        self.path = path
        self.f = None

    def connect(self):
        # Python's open() refuses named pipes on Windows (EINVAL), so go via CreateFileW.
        import ctypes
        import msvcrt
        k32 = ctypes.WinDLL("kernel32", use_last_error=True)
        GENERIC_READ = 0x80000000
        GENERIC_WRITE = 0x40000000
        OPEN_EXISTING = 3
        k32.CreateFileW.restype = ctypes.c_void_p
        k32.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32,
                                    ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32,
                                    ctypes.c_void_p]
        h = k32.CreateFileW(self.path, GENERIC_READ | GENERIC_WRITE, 0, None, OPEN_EXISTING, 0, None)
        if h == ctypes.c_void_p(-1).value or h is None:
            raise OSError(ctypes.get_last_error(), "CreateFileW failed on " + self.path)
        fd = msvcrt.open_osfhandle(h, os.O_BINARY | os.O_RDWR)
        self.f = os.fdopen(fd, "r+b", buffering=0)

    def close(self):
        try:
            self.f.close()
        except Exception:
            pass

    def send_raw(self, payload):
        self.f.write(b"FDG_PROTOCOL\n" + struct.pack("<i", len(payload)) + payload)
        self.f.flush()

    def read_exact(self, n):
        buf = b""
        while len(buf) < n:
            chunk = self.f.read(n - len(buf))
            if not chunk:
                raise EOFError("pipe closed")
            buf += chunk
        return buf

    def read_frame(self):
        header = b""
        while not header.endswith(b"\n"):
            c = self.f.read(1)
            if not c:
                raise EOFError("pipe closed")
            header += c
        header = header.strip()
        kind = header.decode()
        if not kind.startswith("FDG_"):
            raise ValueError("bad header %r" % header)
        if kind == "FDG_PROTOCOL":
            (ln,) = struct.unpack("<i", self.read_exact(4))
            return self.read_exact(ln)
        if kind == "FDG_COMPRESSED":
            import gzip
            (ln,) = struct.unpack("<i", self.read_exact(4))
            return gzip.decompress(self.read_exact(ln))
        if kind == "FDG_CHUNK":
            total, nchunks = struct.unpack("<ii", self.read_exact(8))
            out = b""
            for _ in range(nchunks):
                (ln,) = struct.unpack("<i", self.read_exact(4))
                out += self.read_exact(ln)
            return out
        raise ValueError(kind)

    def request(self, payload, timeout=6.0):
        """Send and wait for one IpcResult with matching cmdId (best effort)."""
        res = {}

        def work():
            try:
                self.send_raw(payload)
                res["data"] = self.read_frame()
            except Exception as ex:  # noqa: BLE001
                res["error"] = ex

        th = threading.Thread(target=work, daemon=True)
        th.start()
        th.join(timeout)
        if th.is_alive():
            raise TimeoutError("no reply in %.1fs" % timeout)
        if "error" in res:
            raise res["error"]
        return res["data"]


def cmd(cmd_id, param_field=None, param_bytes=b""):
    p = f_varint(1, cmd_id)
    if param_field:
        p += f_bytes(param_field, param_bytes)
    return p


def decode_result(buf):
    d = as_dict(buf)
    out = {
        "cmdId": (d.get(1, [0])[0] if 1 in d else None),
        "state": (d.get(2, [0])[0] if 2 in d else None),
        "code": (d.get(3, [0])[0] if 3 in d else None),
    }
    return out, d


def decode_device(buf):
    d = as_dict(buf)
    def s(fn, default=""):
        v = d.get(fn)
        return v[0].decode("utf-8", "replace") if v else default
    def i(fn, default=0):
        v = d.get(fn)
        return v[0] if v else default
    return {
        "productName": s(1),
        "category": i(2),
        "connectType": i(3),
        "deviceCode": s(4),
        "deviceType": i(5),
        "deviceMac": s(6),
        "uid": s(7),
        "isConnected": bool(i(8)),
        "battery": i(9, -1),
        "firmwareVersion": s(10),
        "vendorId": i(11),
        "productId": i(12),
        "isNewArchitecture": bool(i(14)),
    }


def get_connected_devices(cl, timeout=6.0):
    raw = cl.request(cmd(4097, 3, b""), timeout)
    info, d = decode_result(raw)
    devices = []
    inner = d.get(5, [None])[0]
    if inner:
        for fn, wt, v in parse(inner):
            if fn == 1 and wt == 2:
                devices.append(decode_device(v))
    return info, devices


if __name__ == "__main__":
    cl = Client()
    cl.connect()
    try:
        info, devices = get_connected_devices(cl)
        print("result:", info)
        for dv in devices:
            print(dv)
    finally:
        cl.close()
