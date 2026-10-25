import ctypes, sys, time

DLL = r"C:\Program Files\Flydigi Space Station\hidapi.dll"
hid = ctypes.CDLL(DLL)


class hid_device_info(ctypes.Structure):
    pass


hid_device_info._fields_ = [
    ("path", ctypes.c_char_p),
    ("vendor_id", ctypes.c_ushort),
    ("product_id", ctypes.c_ushort),
    ("serial_number", ctypes.c_wchar_p),
    ("release_number", ctypes.c_ushort),
    ("manufacturer_string", ctypes.c_wchar_p),
    ("product_string", ctypes.c_wchar_p),
    ("usage_page", ctypes.c_ushort),
    ("usage", ctypes.c_ushort),
    ("interface_number", ctypes.c_int),
    ("next", ctypes.POINTER(hid_device_info)),
]

hid.hid_enumerate.restype = ctypes.POINTER(hid_device_info)
hid.hid_enumerate.argtypes = [ctypes.c_ushort, ctypes.c_ushort]
hid.hid_free_enumeration.argtypes = [ctypes.POINTER(hid_device_info)]
hid.hid_open_path.restype = ctypes.c_void_p
hid.hid_open_path.argtypes = [ctypes.c_char_p]
hid.hid_close.argtypes = [ctypes.c_void_p]
hid.hid_read_timeout.restype = ctypes.c_int
hid.hid_read_timeout.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t, ctypes.c_int]
hid.hid_get_feature_report.restype = ctypes.c_int
hid.hid_get_feature_report.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_size_t]
hid.hid_error.argtypes = [ctypes.c_void_p]
hid.hid_error.restype = ctypes.c_wchar_p


def enum(vid=0, pid=0):
    head = hid.hid_enumerate(vid, pid)
    cur = head
    out = []
    while cur:
        i = cur.contents
        out.append({
            "path": i.path,
            "vid": i.vendor_id,
            "pid": i.product_id,
            "product": i.product_string,
            "usage_page": i.usage_page,
            "usage": i.usage,
            "iface": i.interface_number,
        })
        cur = i.next
    if head:
        hid.hid_free_enumeration(head)
    return out


devs = enum(0x37D7, 0x2501)
print("flydigi devices:", len(devs))
for d in devs:
    print("iface=%d page=0x%04X usage=0x%04X %s" % (d["iface"], d["usage_page"], d["usage"], d["path"].decode("utf-8", "replace")))

if len(sys.argv) > 1 and sys.argv[1] == "open":
    for d in devs:
        h = hid.hid_open_path(d["path"])
        if not h:
            print("open FAILED page=0x%04X usage=0x%04X" % (d["usage_page"], d["usage"]))
            continue
        print("open OK page=0x%04X usage=0x%04X" % (d["usage_page"], d["usage"]))
        buf = ctypes.create_string_buffer(64)
        n = hid.hid_read_timeout(h, buf, 64, 1500)
        print("   read ->", n, buf.raw[:max(n, 0)].hex(" ") if n > 0 else "")
        fb = ctypes.create_string_buffer(65)
        fb[0] = b"\x00"
        n2 = hid.hid_get_feature_report(h, fb, 65)
        print("   feature(0) ->", n2, fb.raw[:max(n2, 0)].hex(" ") if n2 > 0 else "")
        hid.hid_close(h)
