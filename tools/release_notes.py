#!/usr/bin/env python3
"""Prints the GitHub release notes for a version: an install note plus that version's CHANGELOG.md section.

usage: release_notes.py <version>      (e.g. 1.3.1 or v1.3.1)
"""
import os
import re
import sys

version = sys.argv[1].lstrip("v")
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
text = open(os.path.join(root, "CHANGELOG.md"), encoding="utf-8").read()

section = None
for match in re.finditer(r"^## (\S+)[^\n]*\n(.*?)(?=^## |\Z)", text, re.S | re.M):
    if match.group(1) == version:
        section = match.group(2).strip()
        break

print(f"**For Vintage Story 1.22.x.** Download `vintagee_{version}.zip` below and drop it (still zipped) into the `Mods` folder on the server and on every client.")
print()
if section:
    print("## What's changed")
    print()
    print(section)
else:
    print("See CHANGELOG.md for details.")
