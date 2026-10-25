import json, os
HERE = os.path.dirname(os.path.abspath(__file__))
d=json.load(open(os.path.join(HERE, "schema.json"), encoding="utf-8"))
msgs={}
for f in d:
    for m in f["fd"]["messages"]:
        msgs.setdefault(m["name"],m)
def show(name):
    m=msgs.get(name)
    print("==",name)
    if not m: print("  (missing)"); return
    for fl in m["fields"]:
        print("   %-4s %-10s %-30s %s"%(fl["num"],fl["label"],fl["type"],fl["name"]), fl.get("type_name") or "")
for n in ["IpcCommand","IpcResult","DeviceInfo","ControllerInfo","GetConnectedDevicesResult","GetDeviceDetailInfoResult","GetDeviceDetailInfoParams","HidDevice","ChargerInfo","FirmwareInfo"]:
    show(n)
