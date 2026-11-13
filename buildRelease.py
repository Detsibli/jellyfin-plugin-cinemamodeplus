"""
Build and package a Cinema Mode Plus release.

build.yaml is the single source of truth for the version, changelog and plugin metadata.

    python buildRelease.py --print-version   # print the version from build.yaml
    python buildRelease.py                   # build, zip and add the version to manifest.json

The zip and release notes are written to artifacts/. Used by .github/workflows/release.yml, but works locally too
(requires the .NET SDK and `pip install pyyaml`).
"""

from __future__ import annotations

import argparse
import datetime
import hashlib
import json
import os
import subprocess
import sys
import zipfile

import yaml

REPO_URL = "https://github.com/Detsibli/jellyfin-plugin-cinemamodeplus"
ORIGINAL_REPO_URL = "https://github.com/CherryFloors/jellyfin-plugin-cinemamode"
PROJECT = "Jellyfin.Plugin.CinemaModePlus"
PUBLISH_DIR = "bin"
ARTIFACTS_DIR = "artifacts"
MANIFEST = "manifest.json"
IMAGE_URL = f"{REPO_URL}/raw/main/{PROJECT}/Images/jellyfin-plugin-cinemamodeplus.png"


def load_build_yaml() -> dict:
    with open("build.yaml", "r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def changelog_text(build: dict) -> str:
    return "".join(f"- {line}\n" for line in build.get("changelog", []))


def plugin_info(build: dict) -> dict:
    """Top-level manifest fields describing the plugin."""
    return {
        "category": build["category"],
        "guid": build["guid"],
        "name": build["name"],
        "description": build["description"].strip(),
        "owner": build["owner"],
        "overview": build["overview"],
        "imageUrl": IMAGE_URL,
    }


def build_project(version: str) -> None:
    args = [
        "dotnet", "publish", f"./{PROJECT}/{PROJECT}.csproj",
        "--configuration", "Release",
        "--output", PUBLISH_DIR,
        f"-p:Version={version}",
        f"-p:AssemblyVersion={version}",
        f"-p:FileVersion={version}",
    ]
    subprocess.run(args, check=True)


def package(build: dict, timestamp: str) -> str:
    version = build["version"]
    meta = plugin_info(build)
    meta.update({
        "changelog": changelog_text(build),
        "targetAbi": build["targetAbi"],
        "timestamp": timestamp,
        "version": version,
    })

    os.makedirs(ARTIFACTS_DIR, exist_ok=True)
    zip_path = os.path.join(ARTIFACTS_DIR, zip_name(version))
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        for artifact in build["artifacts"]:
            z.write(os.path.join(PUBLISH_DIR, artifact), artifact)
        z.writestr("meta.json", json.dumps(meta, indent=4))
    return zip_path


def zip_name(version: str) -> str:
    return f"cinemamodeplus_{version}.zip"


def md5(path: str) -> str:
    with open(path, "rb") as f:
        return hashlib.md5(f.read()).hexdigest()


def update_manifest(build: dict, zip_path: str, timestamp: str) -> None:
    """Add this version to manifest.json (newest first) in the format Jellyfin's plugin repositories expect."""
    version = build["version"]
    if os.path.exists(MANIFEST):
        with open(MANIFEST, "r", encoding="utf-8") as f:
            manifest = json.load(f)
    else:
        manifest = []

    entry = next((p for p in manifest if p.get("guid") == build["guid"]), None)
    if entry is None:
        entry = {"versions": []}
        manifest.insert(0, entry)

    versions = [v for v in entry.get("versions", []) if v.get("version") != version]
    entry.clear()
    entry.update(plugin_info(build))
    entry["versions"] = [{
        "version": version,
        "changelog": changelog_text(build),
        "targetAbi": build["targetAbi"],
        "sourceUrl": f"{REPO_URL}/releases/download/v{version}/{zip_name(version)}",
        "checksum": md5(zip_path),
        "timestamp": timestamp,
    }] + versions

    with open(MANIFEST, "w", encoding="utf-8", newline="\n") as f:
        f.write(json.dumps(manifest, indent=4) + "\n")


def write_release_notes(build: dict) -> str:
    path = os.path.join(ARTIFACTS_DIR, "release-notes.md")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(f"## {build['name']} {build['version']}\n\n")
        f.write(f"Targets Jellyfin {build['targetAbi']}.\n\n")
        f.write("### Changes\n\n")
        f.write(changelog_text(build))
        f.write("\n### Install\n\n")
        f.write("In Jellyfin go to **Dashboard > Plugins > Repositories**, add this repository URL, then install "
                f"**{build['name']}** from the catalog:\n\n")
        f.write(f"```\nhttps://raw.githubusercontent.com/Detsibli/jellyfin-plugin-cinemamodeplus/main/{MANIFEST}\n```\n\n")
        f.write(f"Cinema Mode Plus is based on [Cinema Mode]({ORIGINAL_REPO_URL}) by CherryFloors, "
                "released under the MIT License.\n")
    return path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--print-version", action="store_true", help="print the version from build.yaml and exit")
    args = parser.parse_args()

    build = load_build_yaml()
    if args.print_version:
        print(build["version"])
        return 0

    timestamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    print(f"Building {build['name']} {build['version']}...", flush=True)
    build_project(build["version"])
    zip_path = package(build, timestamp)
    update_manifest(build, zip_path, timestamp)
    notes = write_release_notes(build)
    print(f"Package: {zip_path} (md5 {md5(zip_path)})")
    print(f"Release notes: {notes}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
