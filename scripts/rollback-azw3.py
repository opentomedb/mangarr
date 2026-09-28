#!/usr/bin/env python3
"""rollback-azw3.py <readarr.db> [--apply]

Kept so the light-novel formats round's instructions still work: exactly
  rollback-ln-quality.py <readarr.db> --quality 7 [--apply]
See rollback-ln-quality.py for what it rewrites. Dry run by default.
"""
import os
import sys

if __name__ == "__main__":
    script = os.path.join(os.path.dirname(os.path.abspath(__file__)), "rollback-ln-quality.py")
    os.execv(sys.executable, [sys.executable, script, *sys.argv[1:], "--quality", "7"])
