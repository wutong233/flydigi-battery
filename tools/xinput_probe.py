import ctypes
from ctypes import wintypes

x = ctypes.WinDLL("xinput1_4.dll", use_last_error=True)

class XINPUT_GAMEPAD(ctypes.Structure):
    _fields_ = [("wButtons", wintypes.WORD), ("bLeftTrigger", wintypes.BYTE), ("bRightTrigger", wintypes.BYTE),
                ("sThumbLX", ctypes.c_short), ("sThumbLY", ctypes.c_short),
                ("sThumbRX", ctypes.c_short), ("sThumbRY", ctypes.c_short)]

class XINPUT_STATE(ctypes.Structure):
    _fields_ = [("dwPacketNumber", wintypes.DWORD), ("Gamepad", XINPUT_GAMEPAD)]

class XINPUT_BATTERY_INFORMATION(ctypes.Structure):
    _fields_ = [("BatteryType", wintypes.BYTE), ("BatteryLevel", wintypes.BYTE)]

x.XInputGetState.argtypes = [wintypes.DWORD, ctypes.POINTER(XINPUT_STATE)]
x.XInputGetBatteryInformation.argtypes = [wintypes.DWORD, wintypes.BYTE, ctypes.POINTER(XINPUT_BATTERY_INFORMATION)]

for i in range(4):
    st = XINPUT_STATE()
    rc = x.XInputGetState(i, ctypes.byref(st))
    if rc != 0:
        print(i, "not connected", rc)
        continue
    bi = XINPUT_BATTERY_INFORMATION()
    rc2 = x.XInputGetBatteryInformation(i, 0, ctypes.byref(bi))
    print(i, "connected", "pkt", st.dwPacketNumber, "buttons", hex(st.Gamepad.wButtons),
          "batt rc", rc2, "type", bi.BatteryType, "level", bi.BatteryLevel)
