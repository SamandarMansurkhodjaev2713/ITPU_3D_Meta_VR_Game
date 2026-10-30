"""Export accepted Blender v6 roots into a bounded Unity FBX asset.

This script never saves or mutates the source .blend. It must run in a dedicated
background Blender process. Selection is driven by the stable ``itpu_zone``
property on root empties, so excluded rooms cannot leak into the vertical slice.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

import bpy


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
FULL_EXPECTED_COUNTS = {"roots": 380, "render_meshes": 626, "anchors": 51}


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


def export_selected(output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=str(output),
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_anim=False,
        add_leaf_bones=False,
        use_triangles=True,
        path_mode="AUTO",
    )


def main() -> None:
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--zone", default="WORK_403")
    parser.add_argument(
        "--zones",
        help="Comma-separated exact zone allowlist, or FULL_ALLOWED. Overrides --zone.",
    )
    parser.add_argument("--expected-source-sha256")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else [])

    if not bpy.app.background:
        raise RuntimeError("Dedicated background Blender process required")
    if not bpy.data.filepath:
        raise RuntimeError("Load the accepted .blend before export")

    source = Path(bpy.data.filepath).resolve()
    if not source.is_relative_to(root):
        raise RuntimeError("Loaded .blend is outside the workspace")
    output = workspace_path(root, args.output, ".fbx")
    report = workspace_path(root, args.report, ".json")
    if output.exists():
        raise FileExistsError(f"Refuse overwrite: {output}")
    source_digest = sha256(source)
    if args.expected_source_sha256 and source_digest != args.expected_source_sha256:
        raise RuntimeError(
            f"Accepted source hash mismatch: expected {args.expected_source_sha256}, got {source_digest}"
        )

    if bpy.context.scene.unit_settings.system != "METRIC":
        raise RuntimeError("Accepted source is not metric")
    if abs(bpy.context.scene.unit_settings.scale_length - 1.0) > 1e-9:
        raise RuntimeError("Accepted source unit scale is not one metre")

    if args.zones == "FULL_ALLOWED":
        zones = FULL_ALLOWED_ZONES
    elif args.zones:
        zones = tuple(value.strip() for value in args.zones.split(",") if value.strip())
    else:
        zones = (args.zone,)
    if len(zones) != len(set(zones)):
        raise ValueError("Duplicate zones are not allowed")
    unknown = sorted(set(zones) - set(FULL_ALLOWED_ZONES))
    if unknown:
        raise ValueError(f"Zones outside the product allowlist: {unknown}")

    roots = sorted(
        (obj for obj in bpy.data.objects if obj.get("itpu_zone") in zones),
        key=lambda item: item.name,
    )
    if not roots:
        raise RuntimeError(f"No stable roots found for zones {zones}")

    allowed: set[bpy.types.Object] = set(roots)
    for obj in bpy.data.objects:
        parent = obj.parent
        while parent is not None:
            if parent in roots:
                if "__COLLIDER_" not in obj.name and obj.type in {"MESH", "EMPTY"}:
                    allowed.add(obj)
                break
            parent = parent.parent

    bpy.ops.object.select_all(action="DESELECT")
    for obj in allowed:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = roots[0]
    export_selected(output)

    root_ids = [str(item.get("itpu_asset_id", item.name)) for item in roots]
    render_ids = sorted(
        str(item.get("itpu_node_id"))
        for item in allowed
        if item.type == "MESH" and item.get("itpu_node_id")
    )
    anchor_ids = sorted(
        str(item.get("itpu_anchor_id"))
        for item in allowed
        if item.type == "EMPTY" and item.get("itpu_anchor_id")
    )
    if zones == FULL_ALLOWED_ZONES:
        actual = {
            "roots": len(root_ids),
            "render_meshes": len(render_ids),
            "anchors": len(anchor_ids),
        }
        if actual != FULL_EXPECTED_COUNTS:
            raise RuntimeError(
                f"Full-environment inventory mismatch: expected {FULL_EXPECTED_COUNTS}, got {actual}"
            )
    payload = {
        "status": "PASS",
        "blender_version": bpy.app.version_string,
        "source_blend": source.relative_to(root).as_posix(),
        "source_blend_sha256": source_digest,
        "source_saved": False,
        "zone": zones[0] if len(zones) == 1 else None,
        "zones": list(zones),
        "root_count": len(root_ids),
        "render_mesh_count": len(render_ids),
        "anchor_count": len(anchor_ids),
        "stable_root_ids": root_ids,
        "stable_render_ids": render_ids,
        "stable_anchor_ids": anchor_ids,
        "excluded_collider_proxy_names": True,
        "units": "metres",
        "source_up": "Z",
        "fbx_up": "Y",
        "fbx_forward": "-Z",
        "output": output.relative_to(root).as_posix(),
        "output_sha256": sha256(output),
        "unity_import": "NOT_RUN",
        "quest": "NOT_RUN",
    }
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(payload))


if __name__ == "__main__":
    main()
