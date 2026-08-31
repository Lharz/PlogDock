"""Prints the manifest.toml to submit to DalamudPluginsD17 for the current commit.

The version in the csproj is the single source of truth. The changelog section
carrying that same version supplies the release notes, and its absence is an
error rather than an empty field: a version bumped without notes, or notes
written without a bump, is a mistake worth failing on.

Runs anywhere, not just in Actions. Locally it reads the commit from git; in
Actions it takes the one being built.
"""

from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CSPROJ = ROOT / "PlogDock" / "PlogDock.csproj"
CHANGELOG = ROOT / "CHANGELOG.md"

OWNERS = ["Lharz"]
PROJECT_PATH = "PlogDock"
DEFAULT_REPOSITORY = "https://github.com/Lharz/PlogDock"


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


def main() -> None:
    version = plugin_version()
    notes = release_notes(version)

    manifest = "\n".join(
        [
            "[plugin]",
            f'repository = "{repository()}.git"',
            f'commit = "{commit()}"',
            f"owners = [{', '.join(f'\"{o}\"' for o in OWNERS)}]",
            f'project_path = "{PROJECT_PATH}"',
            f"changelog = {toml_multiline(notes)}",
            "",
        ]
    )

    print(manifest, end="")

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if not summary:
        return

    with open(summary, "a", encoding="utf-8") as handle:
        handle.write(f"## manifest.toml for {version}\n\n")
        handle.write("Copy this into ")
        handle.write(f"`testing/live/{PROJECT_PATH}/manifest.toml` ")
        handle.write("in a DalamudPluginsD17 pull request.\n\n")
        handle.write(f"```toml\n{manifest}```\n")


if __name__ == "__main__":
    main()
