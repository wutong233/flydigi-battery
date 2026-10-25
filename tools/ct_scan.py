import re

data = open(r"C:\Program Files\Flydigi Space Station\resources\app.asar", "rb").read().decode("utf-8", "replace")
pats = [r"Dongle:\s*\d", r'"Dongle"\s*:\s*\d', r"ConnectType\s*[=:]\s*\{", r"ConnectType_[A-Za-z]+\s*[=:]\s*-?\d"]
for pat in pats:
    hits = list(re.finditer(pat, data))[:5]
    print("###", pat, len(hits))
    for m in hits:
        s = max(0, m.start() - 200)
        print(data[s:m.end() + 240].replace("\n", " "))
        print("...")
