import json, os, re
HERE = os.path.dirname(os.path.abspath(__file__))
d=json.load(open(os.path.join(HERE, "schema.json"), encoding="utf-8"))
enums={}
msgs={}
for f in d:
    fd=f["fd"]
    for e in fd["enums"]: enums[e["name"]]=e
    for m in fd["messages"]:
        msgs.setdefault(m["name"],m)
        for e in m["enums"]: enums[e["name"]]=e
        for n in m["nested"]:
            msgs.setdefault(n["name"],n)
            for e in n["enums"]: enums[e["name"]]=e
print("enums:", len(enums))
for name,e in enums.items():
    vals=[v for v in e["values"] if v[0] and re.search("att|ower|harge", v[0], re.I)]
    if vals: print(name, vals)
print("--- ReadHardwareFunctionParams ---")
for fl in msgs["ReadHardwareFunctionParams"]["fields"]: print("  ",fl["num"],fl["type"],fl["name"],fl.get("type_name") or "")
print("--- ReadHardwareFunctionResult ---")
for fl in msgs["ReadHardwareFunctionResult"]["fields"]: print("  ",fl["num"],fl["type"],fl["name"])
