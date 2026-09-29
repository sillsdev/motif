"""List failing tests from every TRX file under a folder of downloaded CI test results.

Usage: python trx-failures.py <folder>
Prints one line per test project (its counters) and one line per failed test with its message.
"""
import glob
import os
import sys
import xml.etree.ElementTree as ET

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def main(root: str) -> None:
    for path in sorted(glob.glob(os.path.join(root, "**", "*.trx"), recursive=True)):
        path = path.replace("\\", "/")
        job = path.split("test-results-")[-1].split("/")[0] if "test-results-" in path else ""
        run = ET.parse(path).getroot()
        counters = run.find(".//t:Counters", NS)
        counts = counters.attrib if counters is not None else {}
        print(f"{job} {os.path.basename(path)} total={counts.get('total')} "
              f"passed={counts.get('passed')} failed={counts.get('failed')}")
        for result in run.findall(".//t:UnitTestResult", NS):
            if result.get("outcome") != "Failed":
                continue
            message = result.find(".//t:Message", NS)
            text = (message.text or "").strip().replace("\n", " ")[:400] if message is not None else ""
            print(f"   FAIL {result.get('testName')} | {text}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
