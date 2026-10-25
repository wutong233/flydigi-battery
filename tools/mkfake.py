def varint(n):
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        if n:
            out.append(b | 0x80)
        else:
            out.append(b)
            return bytes(out)


def tag(field, wire):
    return varint((field << 3) | wire)


def ld(field, data):
    if isinstance(data, str):
        data = data.encode()
    return tag(field, 2) + varint(len(data)) + data


def vi(field, n):
    return tag(field, 0) + varint(n)


def device(name, code, uid, cat, ct, battery, connected=True):
    m = b""
    m += ld(1, name)
    m += vi(2, cat)
    m += vi(3, ct)
    m += ld(4, code)
    m += ld(7, bytes.fromhex(uid))
    m += vi(8, 1 if connected else 0)
    m += vi(9, battery)
    return m


d1 = device("Flydigi APEX5 Wireless", "k5", "0123456789abcdef0123456789", 2, 1, 3)
d2 = device("Flydigi Charger", "cd2", "aabbccddeeff001122334455", 5, 1, 1, False)

result = ld(1, d1) + ld(1, d2)
ipc = vi(1, 4097) + vi(2, 1) + vi(3, 0) + ld(5, result)

print("payload_hex:", ipc.hex())
print("len:", len(ipc))
