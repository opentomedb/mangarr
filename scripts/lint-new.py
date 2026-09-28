#!/usr/bin/env python3
"""lint-new.py base.json after.json — findings in `after` that `base` did not have (by file, rule, message)."""
import json, sys
from collections import Counter
def keys(path):
    return Counter((f["filePath"].split("/frontend/", 1)[-1], m.get("ruleId"), m["message"]) for f in json.load(open(path)) for m in f["messages"])
base, after = keys(sys.argv[1]), keys(sys.argv[2])
new = after - base
for (f, r, msg), n in sorted(new.items()):
    print(f"{f}: {r}: {msg} x{n}")
print(f"new findings: {sum(new.values())}")
