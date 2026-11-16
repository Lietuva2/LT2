"""Runs a test project of LT2 in a chosen order and keeps what is needed to read a failure.

    python tools/run_tests.py unit|architecture|integration|e2e [options]

Options:
    --order ORDER     default (xUnit's own), forward, reverse, random or random:SEED (sets LT2_TEST_ORDER; see
                      tests/Shared/TestOrder.cs). A random order without a seed picks one and prints it.
    --class NAME      only this class (repeatable; a short name is completed with the project's namespace).
    --each-class      every class alone, one after another, each in its own process (end-to-end: on the stack as the
                      classes before it left it, without any clean-up between).
    --repeat N        run the whole thing N times in a row.
    --no-build        use the last build.

Results go to TestResults/runs/<time>-<project>-<order>/: the console output, a TRX report with every failure's full
message, the order the classes ran in, and for end-to-end runs the screenshots of failed tests
(tests/Lt2.E2E/bin/.../output/failures). For an end-to-end run the script first prints what tests could leave on the
stack (tests/Lt2.E2E/leftovers.sql), and again at the end, so a test that does not clean up after itself shows.

End-to-end tests need the local stack (docker compose -f deploy/compose.yaml up -d --build); the defaults below are
its addresses, and every LT2_E2E_* variable already set wins. The run makes and deletes its own invitation code.
Works the same in PowerShell and in a Linux shell.
"""

from __future__ import annotations

import argparse
import datetime
import os
import random
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROJECTS = {
    "unit": ("tests/Lt2.UnitTests", "Lt2.UnitTests"),
    "architecture": ("tests/Lt2.ArchitectureTests", "Lt2.ArchitectureTests"),
    "integration": ("tests/Lt2.IntegrationTests", "Lt2.IntegrationTests"),
    "e2e": ("tests/Lt2.E2E", "Lt2.E2E"),
}


def e2e_environment() -> dict[str, str]:
    env = dict(os.environ)
    http_port = env.get("LT2_HTTP_PORT", "8080")
    env.setdefault("LT2_E2E_BASE_URL", "https://lt2.localhost:8443")
    env.setdefault("LT2_E2E_MAILPIT_URL", "http://localhost:8025")
    env.setdefault("LT2_E2E_HTTP_URL", f"http://lt2.localhost:{http_port}")
    return env


def leftovers(compose_file: str) -> list[str]:
    sql = (ROOT / "tests" / "Lt2.E2E" / "leftovers.sql").read_text(encoding="utf-8")
    result = subprocess.run(
        ["docker", "compose", "-f", compose_file, "exec", "-T", "postgres", "sh", "-c",
         'psql -U "$POSTGRES_USER" -d lt2 -At -v ON_ERROR_STOP=1'],
        input=sql, capture_output=True, text=True, encoding="utf-8", cwd=ROOT)
    if result.returncode != 0:
        return [f"(could not count: {result.stderr.strip()})"]
    return [line for line in result.stdout.splitlines() if line.strip()]


def classes_of(project: str, env: dict[str, str]) -> list[str]:
    """The test classes of a built project, as xUnit lists them."""
    result = subprocess.run(
        ["dotnet", "test", "--project", project, "--no-build", "--list-tests"],
        capture_output=True, text=True, encoding="utf-8", cwd=ROOT, env=env)
    names = set()
    for line in result.stdout.splitlines():
        line = line.strip()
        if line.startswith("Lt2.") and "." in line:
            method = line.split("(")[0]
            names.add(method.rsplit(".", 1)[0])
    return sorted(names)


def failures(trx: Path) -> list[str]:
    if not trx.exists():
        return [f"(no report at {trx})"]
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    out = []
    for result in ET.parse(trx).getroot().iterfind(".//t:UnitTestResult", ns):
        if result.get("outcome") not in ("Failed", "Error"):
            continue
        message = result.findtext(".//t:Message", default="", namespaces=ns).strip()
        stack = result.findtext(".//t:StackTrace", default="", namespaces=ns).strip()
        out.append(f"FAILED {result.get('testName')} ({result.get('duration')})\n{message}\n{stack}\n")
    return out


def run_once(args, project: str, assembly: str, env: dict[str, str], out: Path, name: str, classes: list[str]) -> int:
    out.mkdir(parents=True, exist_ok=True)
    env = dict(env)
    env["LT2_TEST_ORDER_LOG"] = str(out / f"{name}-order.txt")
    command = ["dotnet", "test", "--project", PROJECTS[args.project][0], "--no-build",
               "--results-directory", str(out), "--report-xunit-trx", "--report-xunit-trx-filename", f"{name}.trx",
               "--output", "Detailed", "--no-ansi", "--progress", "off"]
    for c in classes:
        command += ["--filter-class", c if c.startswith(assembly + ".") else f"{assembly}.{c}"]
    started = datetime.datetime.now()
    print(f"== {name}: {' '.join(command[2:4])} order={env.get('LT2_TEST_ORDER', 'default')} {' '.join(classes)}", flush=True)
    with open(out / f"{name}.log", "w", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   text=True, encoding="utf-8", errors="replace")
        for line in process.stdout:
            log.write(line)
            if not line.startswith(("  passed ", "passed ", "  skipped ", "skipped ")):
                sys.stdout.write(line)
        code = process.wait()
    found = failures(out / f"{name}.trx")
    print(f"== {name}: exit {code}, {len(found)} failed, {datetime.datetime.now() - started}", flush=True)
    for failure in found:
        print(failure)
    return code


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("project", choices=PROJECTS)
    parser.add_argument("--order", default="default")
    parser.add_argument("--class", dest="classes", action="append", default=[])
    parser.add_argument("--each-class", action="store_true")
    parser.add_argument("--repeat", type=int, default=1)
    parser.add_argument("--no-build", action="store_true")
    args = parser.parse_args()

    project, assembly = PROJECTS[args.project]
    order = args.order
    if order == "random":
        order = f"random:{random.randrange(1, 2**31)}"
    env = e2e_environment() if args.project == "e2e" else dict(os.environ)
    env["LT2_TEST_ORDER"] = order

    if not args.no_build:
        build = subprocess.run(["dotnet", "build", project], cwd=ROOT)
        if build.returncode != 0:
            return build.returncode

    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    out = ROOT / "TestResults" / "runs" / f"{stamp}-{args.project}-{order.replace(':', '-')}"
    compose_file = env.get("LT2_E2E_COMPOSE_FILE", str(ROOT / "deploy" / "compose.yaml"))
    print(f"Results: {out}", flush=True)
    if args.project == "e2e":
        before = leftovers(compose_file)
        print("Before:\n  " + "\n  ".join(before), flush=True)

    worst = 0
    for repeat in range(1, args.repeat + 1):
        if args.each_class:
            for c in args.classes or classes_of(project, env):
                code = run_once(args, project, assembly, env, out, f"{repeat}-{c.rsplit('.', 1)[-1]}", [c])
                worst = worst or code
        else:
            code = run_once(args, project, assembly, env, out, f"run-{repeat}", args.classes)
            worst = worst or code

    if args.project == "e2e":
        after = leftovers(compose_file)
        print("After:\n  " + "\n  ".join(after), flush=True)
        changed = [f"{b}  ->  {a}" for b, a in zip(before, after) if a != b]
        print("Left on the stack by the run: " + ("nothing" if not changed else "\n  " + "\n  ".join(changed)), flush=True)
    print(f"Results: {out}")
    return worst


if __name__ == "__main__":
    sys.exit(main())
