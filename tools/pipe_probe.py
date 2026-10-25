import ctypes, time
k=ctypes.WinDLL("kernel32", use_last_error=True)
k.CreateFileW.restype=ctypes.c_void_p
GENERIC_READ=0x80000000; GENERIC_WRITE=0x40000000; OPEN_EXISTING=3
p=r"\\.\pipe\fcs.sock"
t0=time.time(); tries=0; ok=None
while time.time()-t0 < 8:
    tries+=1
    h=k.CreateFileW(ctypes.c_wchar_p(p), GENERIC_READ|GENERIC_WRITE, 0, None, OPEN_EXISTING, 0, None)
    err=ctypes.get_last_error()
    if h != ctypes.c_void_p(-1).value and h:
        ok=(tries, h); break
    time.sleep(0.2)
print("tries",tries,"success",ok is not None, "last_err", err)
# WaitNamedPipe test
k.WaitNamedPipeW.restype=ctypes.c_int
r=k.WaitNamedPipeW(ctypes.c_wchar_p(p), 1000)
print("WaitNamedPipe ->", r, "err", ctypes.get_last_error())
