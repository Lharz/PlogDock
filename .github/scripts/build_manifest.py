"""Builds the DalamudPluginsD17 submission for the current commit.

Prints the manifest.toml, and with --out writes the whole submission tree so the
artifact unzips straight onto a clone of DalamudPluginsD17.

The version in the csproj is the single source of truth. The changelog section
carrying that same version supplies the release notes, and that section must be
the topmost one: the csproj version is the one being prepared, so notes sitting
above it belong to a version the project has not been bumped to yet and would
ship under the wrong number.

Runs anywhere, not just in Actions. Locally it reads the commit from git; in
Actions it takes the one being built.
"""

from __future__ import annotations

import argparse
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CSPROJ = ROOT / "PlogDock" / "PlogDock.csproj"
CHANGELOG = ROOT / "CHANGELOG.md"
ICON = ROOT / "PlogDock" / "images" / "icon.png"

OWNERS = ["Lharz"]
PROJECT_PATH = "PlogDock"
DEFAULT_REPOSITORY = "https://github.com/Lharz/PlogDock"

# New plugins land in testing before being promoted. Override with D17_CHANNEL
# once this one moves to stable.
CHANNEL = os.environ.get("D17_CHANNEL", "testing/live")

# D17 requires a square icon, no smaller than 64 and no larger than 512.
ICON_MIN, ICON_MAX = 64, 512


def fail(message: str) -> None:
    print(f"error: {message}", file=sys.stderr)
    sys.exit(1)


def plugin_version() -> str:
    match = re.search(r"<Version>\s*([^<\s]+)\s*</Version>", CSPROJ.read_text(encoding="utf-8"))
    if match is None:
        fail(f"no <Version> element in {CSPROJ.relative_to(ROOT)}")

    return match.group(1)


def release_notes(version: str) -> str:
    """The changelog section for this version, without its heading."""
    text = CHANGELOG.read_text(encoding="utf-8")

    headings = re.findall(r"^##\s+(\d[\w.]*)\s*$", text, re.MULTILINE)

    if headings and headings[0] != version:
        fail(
            f"the topmost section of {CHANGELOG.name} is {headings[0]}, but "
            f"<Version> is {version}. The version being prepared must be the "
            f"first section; bump the csproj or move the section down."
        )

    # Headings are matched anchored to a line start so a version mentioned inside
    # a bullet cannot be taken for a section of its own.
    pattern = rf"^##\s+{re.escape(version)}\s*$(.*?)(?=^##\s|\Z)"
    match = re.search(pattern, text, re.MULTILINE | re.DOTALL)

    if match is None:
        fail(
            f"{CHANGELOG.name} has no section for version {version}. "
            f"Add a '## {version}' heading describing the release, or correct "
            f"<Version> in {CSPROJ.relative_to(ROOT)}."
        )

    notes = match.group(1).strip()

    if not notes or notes == "_Nothing yet._":
        fail(f"the {version} section of {CHANGELOG.name} is empty.")

    return notes


def icon_size(path: Path) -> tuple[int, int]:
    """Width and height straight out of the PNG header.

    Read by hand rather than with an imaging library: the dimensions live at a
    fixed offset in the IHDR chunk, and this keeps the workflow free of a
    dependency installed solely to measure one file.
    """
    data = path.read_bytes()

    if data[:8] != b"\x89PNG\r\n\x1a\n":
        fail(f"{path.relative_to(ROOT)} is not a PNG file")

    return int.from_bytes(data[16:20], "big"), int.from_bytes(data[20:24], "big")


def check_icon() -> None:
    if not ICON.is_file():
        fail(f"{ICON.relative_to(ROOT)} is missing")

    width, height = icon_size(ICON)

    if width != height:
        fail(f"the icon must be square, but it is {width}x{height}")

    if not ICON_MIN <= width <= ICON_MAX:
        fail(f"the icon must be between {ICON_MIN} and {ICON_MAX} pixels, but it is {width}x{height}")


def commit() -> str:
    """The commit to pin. Actions supplies it; locally git is asked."""
    sha = os.environ.get("GITHUB_SHA")
    if sha:
        return sha

    try:
        return subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=True,
        ).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        fail("could not determine the commit: not a git repository and GITHUB_SHA is unset")


def repository() -> str:
    server = os.environ.get("GITHUB_SERVER_URL")
    slug = os.environ.get("GITHUB_REPOSITORY")

    # Derived from the environment when available, so a renamed or forked
    # repository stays correct without editing this script.
    return f"{server}/{slug}" if server and slug else DEFAULT_REPOSITORY


def toml_multiline(value: str) -> str:
    """A TOML multi-line basic string.

    Only backslashes and a literal triple quote need escaping; everything else,
    newlines included, is carried verbatim. The leading newline is deliberate:
    TOML discards the first one after the opening delimiter, which keeps the
    rendered value free of a stray blank first line.
    """
    escaped = value.replace("\\", "\\\\").replace('"""', '\\"\\"\\"')
    return f'"""\n{escaped}"""'


def manifest_text(notes: str) -> str:
    owners = ", ".join(f'"{owner}"' for owner in OWNERS)

    return "\n".join(
        [
            "[plugin]",
            f'repository = "{repository()}.git"',
            f'commit = "{commit()}"',
            f"owners = [{owners}]",
            f'project_path = "{PROJECT_PATH}"',
            f"changelog = {toml_multiline(notes)}",
            "",
        ]
    )


def write_submission(out: Path, manifest: str) -> Path:
    """Lays out the tree exactly as DalamudPluginsD17 expects it.

    Rooted at the channel directory so the artifact can be unzipped over a clone
    of the repository with nothing left to move by hand.
    """
    target = out / CHANNEL / PROJECT_PATH
    (target / "images").mkdir(parents=True, exist_ok=True)

    (target / "manifest.toml").write_text(manifest, encoding="utf-8")
    shutil.copy2(ICON, target / "images" / "icon.png")

    return target


def write_summary(version: str, manifest: str) -> None:
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary:
        return

    with open(summary, "a", encoding="utf-8") as handle:
        handle.write(f"## DalamudPluginsD17 submission for {version}\n\n")
        handle.write(
            "Download the `submission` artifact and unzip it over a clone of "
            "DalamudPluginsD17. It already contains "
            f"`{CHANNEL}/{PROJECT_PATH}/manifest.toml` and its icon.\n\n"
        )
        handle.write(f"```toml\n{manifest}```\n")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--out",
        type=Path,
        help="directory to write the submission tree into",
    )
    args = parser.parse_args()

    version = plugin_version()
    notes = release_notes(version)
    check_icon()

    manifest = manifest_text(notes)

    if args.out:
        target = write_submission(args.out, manifest)
        print(f"wrote {target.relative_to(args.out)}/ under {args.out}", file=sys.stderr)

    print(manifest, end="")
    write_summary(version, manifest)


if __name__ == "__main__":
    main()
