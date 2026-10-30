"""Validate a reopened native v6 Blender scene without saving or mutating it."""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import bpy


def main() -> None:
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument("--snapshot", default="art_pipeline/environment_v6/scene_snapshot.json")
    parser.add_argument("--report", default="reports/local/WO-16/blender_reopen_validation_001.json")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :] if "--" in sys.argv else [])

    snapshot_path = (root / args.snapshot).resolve()
    report_path = (root / args.report).resolve()
    if not snapshot_path.is_relative_to(root) or not report_path.is_relative_to(root):
        raise ValueError("Workspace paths only")
    if not bpy.data.filepath:
        raise RuntimeError("Load a saved .blend before validation")
    blend_path = Path(bpy.data.filepath).resolve()
    if not blend_path.is_relative_to(root):
        raise RuntimeError("Loaded .blend is outside the workspace")

    snapshot = json.loads(snapshot_path.read_text(encoding="utf-8"))
    errors: list[str] = []
    warnings: list[str] = []
    expected_zones = {
        "APPROACH_401",
        "APPROACH_423",
        "HALL_LEFT",
        "HALL_RIGHT",
        "HALL_TOP",
        "KITCHEN",
        "NICHE_412",
        "NICHE_413",
        "NICHE_414",
        "RECEPTION_FRONT",
        "WORK_403",
    }

    if bpy.context.scene.unit_settings.system != "METRIC":
        errors.append("Scene units are not METRIC")
    if abs(bpy.context.scene.unit_settings.scale_length - 1.0) > 1e-9:
        errors.append("Scene unit scale is not 1 metre")

    required_collections = {
        "ITPU_GEN_V6",
        "ITPU_ROOTS",
        "ITPU_RENDER",
        "ITPU_ANCHORS",
        "ITPU_COLLIDER_PROXIES",
    }
    missing_collections = sorted(required_collections - set(bpy.data.collections.keys()))
    if missing_collections:
        errors.append("Missing collections: " + ", ".join(missing_collections))

    roots = bpy.data.collections.get("ITPU_ROOTS")
    renders = bpy.data.collections.get("ITPU_RENDER")
    anchors = bpy.data.collections.get("ITPU_ANCHORS")
    colliders = bpy.data.collections.get("ITPU_COLLIDER_PROXIES")

    expected_roots = {item["id"]: item for item in snapshot["roots"]}
    actual_root_names = set(roots.objects.keys()) if roots else set()
    if actual_root_names != set(expected_roots):
        errors.append("Root name set differs from snapshot")
    if roots:
        for name, source in expected_roots.items():
            obj = roots.objects.get(name)
            if obj is None:
                continue
            if obj.type != "EMPTY":
                errors.append(f"Root {name} is not EMPTY")
            if obj.get("itpu_asset_id") != name:
                errors.append(f"Root {name} lost stable asset id")
            if obj.get("itpu_zone") != source["zone"]:
                errors.append(f"Root {name} zone mismatch")
            if source["zone"] not in expected_zones:
                errors.append(f"Root {name} uses excluded/unknown zone {source['zone']}")
            if any(abs(a - b) > 1e-6 for a, b in zip(obj.location, source["position_m"])):
                errors.append(f"Root {name} location mismatch")
            if abs(math.degrees(obj.rotation_euler.z) - source["yaw_deg"]) > 1e-5:
                errors.append(f"Root {name} yaw mismatch")

    expected_nodes = {"VIS__" + item["name"]: item for item in snapshot["nodes"]}
    actual_render_names = set(renders.objects.keys()) if renders else set()
    if actual_render_names != set(expected_nodes):
        errors.append("Render node name set differs from snapshot")
    uv0_meshes = 0
    uv1_meshes = 0
    if renders:
        for name, source in expected_nodes.items():
            obj = renders.objects.get(name)
            if obj is None:
                continue
            source_id = name.removeprefix("VIS__")
            if obj.type != "MESH":
                errors.append(f"Render node {name} is not MESH")
                continue
            if obj.get("itpu_node_id") != source_id:
                errors.append(f"Render node {name} lost stable node id")
            if obj.parent is None or obj.parent.name != source["root"]:
                errors.append(f"Render node {name} parent mismatch")
            if any(abs(value) > 1e-9 for value in obj.location):
                errors.append(f"Render node {name} has unexpected child translation")
            if "UV0" not in obj.data.uv_layers:
                errors.append(f"Render node {name} has no UV0")
            else:
                uv0_meshes += 1
            if len(obj.data.uv_layers) > 1:
                uv1_meshes += 1
            if obj.get("itpu_uv1") != "NOT_GENERATED":
                errors.append(f"Render node {name} lost explicit UV1 pending marker")
            if len(obj.data.materials) != 1:
                errors.append(f"Render node {name} material slot count is not one")
            elif obj.data.materials[0].name != "ITPU_" + source["material"]:
                errors.append(f"Render node {name} material mismatch")

    expected_anchors = {item["name"]: item for item in snapshot["anchors"]}
    actual_anchor_names = set(anchors.objects.keys()) if anchors else set()
    if actual_anchor_names != set(expected_anchors):
        errors.append("Anchor name set differs from snapshot")
    max_anchor_world_error = 0.0
    if anchors:
        for name, source in expected_anchors.items():
            obj = anchors.objects.get(name)
            if obj is None:
                continue
            if obj.parent is None or obj.parent.name != source["root"]:
                errors.append(f"Anchor {name} parent mismatch")
            if any(abs(a - b) > 1e-6 for a, b in zip(obj.location, source["local_position_m"])):
                errors.append(f"Anchor {name} local position mismatch")
            world = obj.matrix_world.translation
            max_anchor_world_error = max(
                max_anchor_world_error,
                math.dist(tuple(world), tuple(source["location_m"])),
            )
    if max_anchor_world_error > 5e-6:
        errors.append(f"Anchor world reconstruction error {max_anchor_world_error}")

    expected_collider_count = sum(len(item.get("colliders", [])) for item in snapshot["roots"])
    if colliders and len(colliders.objects) != expected_collider_count:
        errors.append("Collider proxy count differs from snapshot")

    missing_images: list[str] = []
    non_relative_images: list[str] = []
    image_color_spaces: dict[str, str] = {}
    for image in bpy.data.images:
        if image.source != "FILE" or not image.filepath:
            continue
        image_color_spaces[image.name] = image.colorspace_settings.name
        if not image.filepath.startswith("//"):
            non_relative_images.append(image.filepath)
        if not Path(bpy.path.abspath(image.filepath)).is_file():
            missing_images.append(image.filepath)
    if missing_images:
        errors.append("Missing external images: " + ", ".join(missing_images))
    if non_relative_images:
        errors.append("Non-relative image paths: " + ", ".join(non_relative_images))

    material_names = set(bpy.data.materials.keys())
    expected_materials = {"ITPU_" + name for name in snapshot["materials"]}
    if material_names != expected_materials:
        errors.append("Material set differs from snapshot")
    required_distinct = {"ITPU_oak", "ITPU_fabric", "ITPU_ceramic", "ITPU_steel", "ITPU_frame"}
    if not required_distinct.issubset(material_names):
        errors.append("Required material classes are not distinct")

    coffee_required = {
        "K_COFFEE__cup_socket",
        "K_COFFEE__stream_origin",
        "K_COFFEE__stream_0",
        "K_COFFEE__stream_1",
        "K_CUP__dock_base",
        "K_CUP__rim_center",
        "K_CUP__grip_left",
        "K_CUP__grip_right",
        "K_CUP__liquid_surface",
    }
    if not coffee_required.issubset(actual_anchor_names):
        errors.append("Coffee/cup anchor contract is incomplete")

    if uv1_meshes:
        warnings.append("UV1 layers exist unexpectedly; verify lightmap policy")
    report = {
        "status": "PASS" if not errors else "FAIL",
        "blender_version": bpy.app.version_string,
        "blend": blend_path.relative_to(root).as_posix(),
        "reopened_in_new_process": True,
        "units": bpy.context.scene.unit_settings.system,
        "scale_length": bpy.context.scene.unit_settings.scale_length,
        "roots": len(actual_root_names),
        "render_meshes": len(actual_render_names),
        "anchors": len(actual_anchor_names),
        "collider_proxies": len(colliders.objects) if colliders else 0,
        "uv0_meshes": uv0_meshes,
        "uv1_meshes": uv1_meshes,
        "max_anchor_world_error_m": max_anchor_world_error,
        "zones": sorted({item["zone"] for item in snapshot["roots"]}),
        "materials": sorted(material_names),
        "external_images": image_color_spaces,
        "missing_external_images": missing_images,
        "errors": errors,
        "warnings": warnings,
        "unity": "NOT_RUN",
        "quest": "NOT_RUN",
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report))
    if errors:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
