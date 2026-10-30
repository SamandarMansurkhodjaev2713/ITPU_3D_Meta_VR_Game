"""Create the numbered v005 Blender successor from the immutable accepted v004 file.

Run only in a dedicated Blender background process with v004 already loaded. The
script narrows and relocates the three niche seats; no other authored object is
changed. It refuses to overwrite any output.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy


PARENT_SHA256 = "4b20976e2e6d330ee760a39e51273af062a0cb87411847d05c51d8393f961331"
SOURCE_REL = "art_pipeline/environment_v6/blender/ITPU_environment_v6_lenovo32_004.blend"
OUTPUT_REL = "art_pipeline/environment_v6/blender/ITPU_environment_v6_lenovo32_005.blend"
ORIGINAL_WIDTH_M = 2.30
TARGET_WIDTH_M = 0.58
TARGETS = {
    "SEAT_412": (6.38, 24.20, 0.0),
    "SEAT_413": (9.23, 24.20, 0.0),
    "SEAT_414": (12.08, 24.20, 0.0),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def workspace_path(root: Path, value: str, suffix: str) -> Path:
    candidate = Path(value)
    if candidate.is_absolute() or ".." in candidate.parts:
        raise ValueError("Relative workspace paths only")
    resolved = (root / candidate).resolve()
    if not resolved.is_relative_to(root) or resolved.suffix.lower() != suffix:
        raise ValueError(f"Invalid workspace {suffix} path: {value}")
    return resolved


def descendants(root: bpy.types.Object) -> list[bpy.types.Object]:
    result: list[bpy.types.Object] = []
    pending = list(root.children)
    while pending:
        item = pending.pop()
        result.append(item)
        pending.extend(item.children)
    return result


def main() -> None:
    workspace = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", default=OUTPUT_REL)
    parser.add_argument("--expected-source-sha256", default=PARENT_SHA256)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else [])

    if not bpy.app.background:
        raise RuntimeError("Dedicated Blender background process required")
    if not bpy.data.filepath:
        raise RuntimeError("Load the immutable v004 file before refinement")
    source = Path(bpy.data.filepath).resolve()
    expected_source = (workspace / SOURCE_REL).resolve()
    if source != expected_source:
        raise RuntimeError(f"Expected exact v004 source {expected_source}, got {source}")
    source_hash = sha256(source)
    if source_hash != args.expected_source_sha256.lower():
        raise RuntimeError(f"v004 hash mismatch: expected {args.expected_source_sha256}, got {source_hash}")

    output = workspace_path(workspace, args.output, ".blend")
    if output.exists():
        raise FileExistsError(f"Refuse overwrite: {output}")
    if not output.name.endswith("_005.blend"):
        raise ValueError("Numbered successor filename must end in _005.blend")
    if bpy.context.scene.unit_settings.system != "METRIC" or abs(
        bpy.context.scene.unit_settings.scale_length - 1.0
    ) > 1e-9:
        raise RuntimeError("Source scene must remain metric at one metre")

    roots = [item for item in bpy.data.objects if item.get("itpu_asset_id")]
    meshes = [item for item in bpy.data.objects if item.type == "MESH" and item.get("itpu_node_id")]
    if len(roots) != 380 or len(meshes) != 626:
        raise RuntimeError(f"Immutable source inventory mismatch: roots={len(roots)}, meshes={len(meshes)}")

    scale_x = TARGET_WIDTH_M / ORIGINAL_WIDTH_M
    changed_meshes: list[str] = []
    changed_colliders: list[str] = []
    seat_rows: list[dict[str, object]] = []
    for identifier, position in TARGETS.items():
        seat = bpy.data.objects.get(identifier)
        if seat is None or seat.get("itpu_asset_id") != identifier:
            raise RuntimeError(f"Missing stable seat root {identifier}")
        if any(abs(float(value) - 1.0) > 1e-7 for value in seat.scale):
            raise RuntimeError(f"Non-unit source root scale on {identifier}")
        children = descendants(seat)
        seat_meshes = [item for item in children if item.type == "MESH"]
        if len(seat_meshes) != 3:
            raise RuntimeError(f"Expected three render meshes below {identifier}, got {len(seat_meshes)}")
        for mesh_object in seat_meshes:
            mesh = mesh_object.data
            if mesh.users != 1:
                mesh_object.data = mesh.copy()
                mesh = mesh_object.data
            for vertex in mesh.vertices:
                vertex.co.x *= scale_x
            mesh.update()
            changed_meshes.append(str(mesh_object.get("itpu_node_id", mesh_object.name)))

        proxies = [item for item in children if "collider_json" in item]
        if len(proxies) != 3:
            raise RuntimeError(f"Expected three collider proxies below {identifier}, got {len(proxies)}")
        for proxy in proxies:
            collider = json.loads(proxy["collider_json"])
            collider["center_m"][0] *= scale_x
            collider["size_m"][0] *= scale_x
            proxy["collider_json"] = json.dumps(collider, separators=(",", ":"))
            proxy.location.x *= scale_x
            proxy.scale.x *= scale_x
            changed_colliders.append(proxy.name)

        seat.location = position
        seat.rotation_euler = (0.0, 0.0, math.radians(90.0))
        seat["itpu_refinement_revision"] = "005"
        seat["itpu_refinement_reason"] = "owner_video_side_seating"
        seat_rows.append(
            {
                "id": identifier,
                "position_m": list(position),
                "yaw_deg": 90.0,
                "authored_width_m": TARGET_WIDTH_M,
                "root_scale": [float(value) for value in seat.scale],
            }
        )

    bpy.context.scene["itpu_revision"] = "005"
    bpy.context.scene["itpu_parent_blend"] = SOURCE_REL
    bpy.context.scene["itpu_parent_blend_sha256"] = source_hash
    bpy.context.scene["itpu_refinement_scope"] = "NICHE_SIDE_SEATING_ONLY"
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)
    if not output.is_file():
        raise RuntimeError("Blender save did not create the v005 successor")

    report = {
        "status": "PASS_SAVED_REOPEN_REQUIRED",
        "blender_version": bpy.app.version_string,
        "source": SOURCE_REL,
        "source_sha256": source_hash,
        "output": output.relative_to(workspace).as_posix(),
        "output_sha256": sha256(output),
        "source_saved": False,
        "root_count": len(roots),
        "render_mesh_count": len(meshes),
        "changed_render_meshes": sorted(changed_meshes),
        "changed_collider_proxies": sorted(changed_colliders),
        "seats": seat_rows,
        "unity": "NOT_RUN",
        "quest": "NOT_RUN",
    }
    report_path = output.with_suffix(".report.json")
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))


if __name__ == "__main__":
    main()
