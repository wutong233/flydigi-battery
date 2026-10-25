import ctypes, sys

DLL = r"C:\Program Files\Flydigi Space Station\hidapi.dll"
hid = ctypes.CDLL(DLL)


class hid_device_info(ctypes.Structure):
    pass


hid_device_info._fields_ = [
    ("path", ctypes.c_wchar_p),
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
            "mfr": i.manufacturer_string,
            "usage_page": i.usage_page,
            "usage": i.usage,
            "iface": i.interface_number,
        })
        cur = i.next
    if head:
        hid.hid_free_enumeration(head)
    return out


if __name__ == "__main__":
    devs = enum()
    print("total HID devices:", len(devs))
    fly = [d for d in devs if d["vid"] in (0x37D7, 0x2D5F, 0x0000) or (d["product"] and "flydigi" in (d["product"] or "").lower())]
    for d in fly:
        print("%04X:%04X iface=%d usage_page=0x%04X usage=0x%04X %r %r" % (
            d["vid"], d["pid"], d["iface"], d["usage_page"], d["usage"], d["product"], d["path"]))
    rest = [d for d in devs if d not in fly]
    print("others:", len(rest))
    for d in rest[:10]:
        print("  %04X:%04X usage_page=0x%04X usage=0x%04X %r" % (d["vid"], d["pid"], d["usage_page"], d["usage"], d["product"]))
