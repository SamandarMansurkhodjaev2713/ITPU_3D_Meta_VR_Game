"""Prepare deterministic, non-private Unity transfer data.

This is a mechanical asset step: it extracts only WORK_403 collider/material
metadata from the accepted snapshot and copies the project-owned textures.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from pathlib import Path


FULL_ALLOWED_ZONES = (
    "WORK_403",
    "HALL_LEFT",
    "APPROACH_401",
    "RECEPTION_FRONT",
    "HALL_TOP",
    "NICHE_412",
    "NICHE_413",
    "NICHE_414",
    "HALL_RIGHT",
    "APPROACH_423",
    "KITCHEN",
)
FULL_EXPECTED = {
    "roots": 380,
    "colliders": 554,
    "boxes": 549,
    "static_mesh_proxies": 4,
    "capsule_proxies": 1,
    "teleport_surfaces": 28,
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def vec(values: list[float]) -> dict[str, float]:
    return {"x": values[0], "y": values[2], "z": -values[1]}


def size(values: list[float]) -> dict[str, float]:
    return {"x": values[0], "y": values[2], "z": values[1]}


def main() -> None:
    workspace = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", default="unity/ITPU_VR_Unity")
    parser.add_argument("--report", default="reports/local/WO-17/unity_transfer_data.json")
    parser.add_argument("--data", default="Assets/ITPU/Data/itpu_403_transfer.json")
    parser.add_argument("--scope", choices=("WORK_403", "FULL_ALLOWED"), default="WORK_403")
    args = parser.parse_args()

    project = (workspace / args.project).resolve()
    report_path = (workspace / args.report).resolve()
    data_path = (project / args.data).resolve()
    if (not project.is_relative_to(workspace) or not report_path.is_relative_to(workspace) or
            not data_path.is_relative_to(project) or data_path.suffix.lower() != ".json"):
        raise ValueError("Workspace paths only")

    source_path = workspace / "art_pipeline/environment_v6/scene_snapshot.json"
    blend_path = workspace / "art_pipeline/environment_v6/blender/ITPU_environment_v6_lenovo32_004.blend"
    snapshot = json.loads(source_path.read_text(encoding="utf-8"))
    allowed_zones = FULL_ALLOWED_ZONES if args.scope == "FULL_ALLOWED" else ("WORK_403",)
    roots = []
    for item in snapshot["roots"]:
        if item["zone"] not in allowed_zones:
            continue
        colliders = []
        for collider in item.get("colliders", []):
            shape = collider.get("shape")
            converted = {
                "name": collider["name"],
                "shape": shape,
            }
            if "center_m" in collider:
                converted["center"] = vec(collider["center_m"])
            if "size_m" in collider:
                converted["size"] = size(collider["size_m"])
            if "mesh_source" in collider:
                converted["mesh_source"] = collider["mesh_source"]
            if "convex" in collider:
                converted["convex"] = collider["convex"]
            if shape not in {"box", "static_mesh_proxy", "capsule_proxy"}:
                raise ValueError(f"Unsupported collider shape: {shape}")
            colliders.append(converted)
        roots.append({
            "id": item["id"],
            "position": vec(item["position_m"]),
            "yaw_degrees": item["yaw_deg"],
            "movable": item["movable"],
            "teleport_surface": item["id"].startswith("FLOOR_"),
            "colliders": colliders,
        })

    texture_target = project / "Assets/ITPU/Art/Textures"
    texture_target.mkdir(parents=True, exist_ok=True)
    copied_textures = []
    for definition in snapshot.get("textures", {}).values():
        for key in ("base", "unity_mask"):
            source = (source_path.parent / definition[key]).resolve()
            target = texture_target / source.name
            shutil.copy2(source, target)
            copied_textures.append({
                "path": target.relative_to(workspace).as_posix(),
                "sha256": sha256(target),
            })

    materials = []
    for identifier, definition in snapshot["materials"].items():
        textures = snapshot.get("textures", {}).get(identifier, {})
        materials.append({
            "id": identifier,
            "color_hex": "#" + definition["srgb"].lstrip("#"),
            "roughness": definition["roughness"],
            "metallic": definition["metallic"],
            "base_texture": Path(textures["base"]).name if textures else "",
            "mask_texture": Path(textures["unity_mask"]).name if textures else "",
        })

    collider_shapes = [collider["shape"] for root in roots for collider in root["colliders"]]
    counts = {
        "roots": len(roots),
        "colliders": len(collider_shapes),
        "boxes": collider_shapes.count("box"),
        "static_mesh_proxies": collider_shapes.count("static_mesh_proxy"),
        "capsule_proxies": collider_shapes.count("capsule_proxy"),
        "teleport_surfaces": sum(1 for root in roots if root["teleport_surface"]),
    }
    if args.scope == "FULL_ALLOWED" and counts != FULL_EXPECTED:
        raise RuntimeError(f"Full transfer inventory mismatch: expected {FULL_EXPECTED}, got {counts}")

    payload = {
        "schema_version": 2,
        "status": "PREPARED_FOR_NATIVE_UNITY_IMPORT",
        "source_snapshot": source_path.relative_to(workspace).as_posix(),
        "source_snapshot_sha256": sha256(source_path),
        "source_blend": blend_path.relative_to(workspace).as_posix(),
        "source_blend_sha256": sha256(blend_path),
        "scope": args.scope,
        "zones": list(allowed_zones),
        "coordinate_mapping": "(x,y,z)->(x,z,-y)",
        "roots": roots,
        "materials": materials,
        "collider_counts": counts,
    }
    data_path.parent.mkdir(parents=True, exist_ok=True)
    data_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    receipt = {
        "status": "PASS",
        "data": data_path.relative_to(workspace).as_posix(),
        "data_sha256": sha256(data_path),
        "scope": args.scope,
        "zones": list(allowed_zones),
        "counts": counts,
        "copied_textures": sorted(copied_textures, key=lambda item: item["path"]),
        "unity_import": "NOT_RUN",
        "quest": "NOT_RUN",
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt))


if __name__ == "__main__":
    main()
