"""Print the faulting-thread stack of every macOS crash report (.ips) under a folder.

Usage: python ips-stack.py <folder>
An .ips file is one JSON header line followed by a JSON body.
"""
import glob
import json
import os
import sys


def main(root: str) -> None:
    for path in sorted(glob.glob(os.path.join(root, "**", "*.ips"), recursive=True)):
        with open(path, encoding="utf-8") as report:
            report.readline()
            body = json.loads(report.read())
        images = body.get("usedImages", [])
        thread = body["threads"][body.get("faultingThread", 0)]
        print(f"== {os.path.basename(path)}: {body.get('exception', {}).get('signal')} "
              f"{body.get('termination', {}).get('indicator', '')}")
        for frame in thread.get("frames", [])[:30]:
            index = frame.get("imageIndex")
            image = images[index].get("name", "?") if index is not None and index < len(images) else "?"
            print(f"   {image} {frame.get('symbol', '?')}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
