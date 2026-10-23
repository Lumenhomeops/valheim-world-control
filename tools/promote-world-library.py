#!/usr/bin/env python3
import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path

PROFILE_IDS = {
    "Casual": ("casual", "Casual", False),
    "Easy": ("easy", "Easy", False),
    "Hammer_mode": ("hammer-mode", "Hammer Mode", False),
    "Hard": ("hard", "Hard", False),
    "Hardcore": ("hardcore", "Hardcore", False),
    "Immersive": ("immersive", "Immersive", False),
    "Normal": ("normal", "Normal", True),
}


def slug(value: str) -> str:
    result = re.sub(r"[^a-z0-9]+", "-", value.lower()).strip("-")
    if not result or len(result) > 64:
        raise ValueError(f"Unsupported library name: {value}")
    return result


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def copy_verified(source: Path, destination: Path) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    if source.stat().st_size != destination.stat().st_size or sha256(source) != sha256(destination):
        raise IOError(f"Verification failed for {source}")


def promote(source_root: Path, library_root: Path) -> list[dict]:
    worlds = []
    for source_world in sorted((path for path in source_root.iterdir() if path.is_dir()), key=lambda path: path.name.lower()):
        databases = list(source_world.glob("*.db"))
        if len(databases) != 1:
            raise ValueError(f"{source_world} must contain exactly one database")
        database = databases[0]
        world_id = slug(source_world.name)
        destination = library_root / world_id
        if destination.exists():
            raise FileExistsError(f"Refusing to overwrite existing library: {destination}")
        temporary = library_root / f".{world_id}.promoting"
        if temporary.exists():
            raise FileExistsError(f"Incomplete prior promotion exists: {temporary}")

        profiles = []
        try:
            copy_verified(database, temporary / "source" / database.name)
            for source_name, (profile_id, display_name, original) in PROFILE_IDS.items():
                profile_source = source_world / "Profiles" / source_name / f"{database.stem}.fwl"
                if not profile_source.is_file():
                    raise FileNotFoundError(profile_source)
                copy_verified(profile_source, temporary / "profiles" / profile_id / f"{database.stem}.fwl")
                profiles.append({"id": profile_id, "displayName": display_name, "isOriginal": original})
            temporary.rename(destination)
        except Exception:
            shutil.rmtree(temporary, ignore_errors=True)
            raise

        worlds.append({
            "id": world_id,
            "displayName": source_world.name,
            "runtimeName": database.stem,
            "databaseBytes": database.stat().st_size,
            "profiles": profiles,
        })
    return worlds


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True, type=Path)
    parser.add_argument("--library", required=True, type=Path)
    parser.add_argument("--catalog", required=True, type=Path)
    args = parser.parse_args()

    with args.catalog.open("r", encoding="utf-8") as stream:
        current = json.load(stream)
    promoted = promote(args.source, args.library)
    existing_ids = {world["id"] for world in current["worlds"]}
    duplicate_ids = existing_ids.intersection(world["id"] for world in promoted)
    if duplicate_ids:
        raise ValueError(f"Catalog already contains: {', '.join(sorted(duplicate_ids))}")
    catalog = {"worlds": current["worlds"] + promoted, "modPacks": current["modPacks"]}
    temporary = args.catalog.with_suffix(".json.new")
    with temporary.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(catalog, stream, indent=2)
        stream.write("\n")
        stream.flush()
    temporary.replace(args.catalog)
    print(f"PROMOTED_WORLDS={len(promoted)}")
    print(f"CATALOG_WORLDS={len(catalog['worlds'])}")
    print(f"CATALOG_PROFILES={sum(len(world['profiles']) for world in catalog['worlds'])}")


if __name__ == "__main__":
    main()
