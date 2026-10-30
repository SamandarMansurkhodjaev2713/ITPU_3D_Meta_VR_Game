"""Validate a reopened v005 Blender scene and the bounded niche-seat refinement."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


PARENT_SHA256 = "4b20976e2e6d330ee760a39e51273af062a0cb87411847d05c51d8393f961331"
TARGETS = {
    "SEAT_412": {"position": (6.38, 24.20, 0.0), "niche": (6.02, 23.45, 8.73, 24.85)},
    "SEAT_413": {"position": (9.23, 24.20, 0.0), "niche": (8.87, 23.45, 11.58, 24.85)},
    "SEAT_414": {"position": (12.08, 24.20, 0.0), "niche": (11.72, 23.45, 14.43, 24.85)},
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def descendants(root: bpy.types.Object) -> list[bpy.types.Object]:
    result: list[bpy.types.Object] = []
    pending = list(root.children)
    while pending:
        item = pending.pop()
        result.append(item)
        pending.extend(item.children)
    return result


def bounds(objects: list[bpy.types.Object]) -> tuple[Vector, Vector]:
    points = [item.matrix_world @ Vector(corner) for item in objects for corner in item.bound_box]
    return (
        Vector((min(point.x for point in points), min(point.y for point in points), min(point.z for point in points))),
        Vector((max(point.x for point in points), max(point.y for point in points), max(point.z for point in points))),
    )


def main() -> None:
    workspace = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument("--report", required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else [])
    report_path = (workspace / args.report).resolve()
    if not report_path.is_relative_to(workspace) or report_path.suffix.lower() != ".json":
        raise ValueError("Workspace JSON report path required")
    if not bpy.app.background or not bpy.data.filepath:
        raise RuntimeError("Validation requires a separately reopened background Blender file")
    blend = Path(bpy.data.filepath).resolve()
    if not blend.is_relative_to(workspace) or not blend.name.endswith("_005.blend"):
        raise RuntimeError("Expected the numbered workspace v005 Blender file")

    errors: list[str] = []
    roots = [item for item in bpy.data.objects if item.get("itpu_asset_id")]
    meshes = [item for item in bpy.data.objects if item.type == "MESH" and item.get("itpu_node_id")]
    proxies = [item for item in bpy.data.objects if "collider_json" in item]
    if len(roots) != 380:
        errors.append(f"Expected 380 roots, found {len(roots)}")
    if len(meshes) != 626:
        errors.append(f"Expected 626 render meshes, found {len(meshes)}")
    if len(proxies) != 554:
        errors.append(f"Expected 554 collider proxies, found {len(proxies)}")
    if bpy.context.scene.unit_settings.system != "METRIC" or abs(
        bpy.context.scene.unit_settings.scale_length - 1.0
    ) > 1e-9:
        errors.append("Metric one-metre scene contract changed")
    if bpy.context.scene.get("itpu_revision") != "005":
        errors.append("Missing v005 scene revision marker")
    if bpy.context.scene.get("itpu_parent_blend_sha256") != PARENT_SHA256:
        errors.append("Immutable v004 parent hash marker mismatch")

    seat_rows: list[dict[str, object]] = []
    for identifier, expected in TARGETS.items():
        seat = bpy.data.objects.get(identifier)
        if seat is None:
            errors.append(f"Missing {identifier}")
            continue
        position = tuple(float(value) for value in seat.location)
        if any(abs(actual - wanted) > 1e-5 for actual, wanted in zip(position, expected["position"])):
            errors.append(f"{identifier} position mismatch: {position}")
        yaw = math.degrees(seat.rotation_euler.z) % 360.0
        if abs(yaw - 90.0) > 1e-4:
            errors.append(f"{identifier} yaw mismatch: {yaw}")
        if any(abs(float(value) - 1.0) > 1e-7 for value in seat.scale):
            errors.append(f"{identifier} root scale is not unit: {tuple(seat.scale)}")
        render_children = [item for item in descendants(seat) if item.type == "MESH"]
        if len(render_children) != 3:
            errors.append(f"{identifier} expected three render children")
            continue
        low, high = bounds(render_children)
        x1, y1, x2, y2 = expected["niche"]
        if low.x < x1 + 0.08 or high.x > x2 - 0.08 or low.y < y1 + 0.02 or high.y > y2 - 0.10:
            errors.append(f"{identifier} leaves safe niche envelope: low={tuple(low)}, high={tuple(high)}")
        if (high.y - low.y) > 0.62 or (high.x - low.x) > 0.52:
            errors.append(f"{identifier} footprint is not compact side seating: {tuple(high - low)}")
        seat_rows.append(
            {
                "id": identifier,
                "position_m": list(position),
                "yaw_deg": yaw,
                "world_bounds_min_m": list(low),
                "world_bounds_max_m": list(high),
            }
        )

    missing_images = []
    for image in bpy.data.images:
        if image.source == "FILE" and image.filepath:
            resolved = Path(bpy.path.abspath(image.filepath)).resolve()
            if not resolved.is_file():
                missing_images.append(image.filepath)
    if missing_images:
        errors.append("Missing external images: " + ", ".join(sorted(missing_images)))

    payload = {
        "status": "PASS_REOPENED" if not errors else "FAIL",
        "blender_version": bpy.app.version_string,
        "blend": blend.relative_to(workspace).as_posix(),
        "blend_sha256": sha256(blend),
        "parent_blend_sha256": bpy.context.scene.get("itpu_parent_blend_sha256"),
        "root_count": len(roots),
        "render_mesh_count": len(meshes),
        "collider_proxy_count": len(proxies),
        "seats": seat_rows,
        "missing_external_images": missing_images,
        "errors": errors,
        "unity": "NOT_RUN",
        "quest": "NOT_RUN",
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(payload))
    if errors:
        raise RuntimeError("; ".join(errors))


if __name__ == "__main__":
    main()
