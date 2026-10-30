"""Derive the versioned Unity transfer contract for the v005 niche-seat successor."""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
from pathlib import Path


PARENT_BLEND_SHA256 = "4b20976e2e6d330ee760a39e51273af062a0cb87411847d05c51d8393f961331"
PARENT_TRANSFER_SHA256 = "ce45d682a56d9df58a3a63e2089d3c6edd8e780f366056456b30bbb629ee1615"
ORIGINAL_WIDTH_M = 2.30
TARGET_WIDTH_M = 0.58
TARGETS = {
    "SEAT_412": (6.38, 0.0, -24.20),
    "SEAT_413": (9.23, 0.0, -24.20),
    "SEAT_414": (12.08, 0.0, -24.20),
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


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output", default="unity/ITPU_VR_Unity/Assets/ITPU/Data/itpu_environment_v005_transfer.json"
    )
    parser.add_argument("--report", default="reports/local/WO-25/unity_transfer_data_v005.json")
    args = parser.parse_args()
    output = workspace_path(root, args.output, ".json")
    report = workspace_path(root, args.report, ".json")
    if output.exists() or report.exists():
        raise FileExistsError("Refuse overwrite of v005 transfer output/report")

    parent_transfer = root / "unity/ITPU_VR_Unity/Assets/ITPU/Data/itpu_environment_v004_transfer.json"
    parent_blend = root / "art_pipeline/environment_v6/blender/ITPU_environment_v6_lenovo32_004.blend"
    successor_blend = root / "art_pipeline/environment_v6/blender/ITPU_environment_v6_lenovo32_005.blend"
    if sha256(parent_transfer) != PARENT_TRANSFER_SHA256:
        raise RuntimeError("Immutable v004 transfer hash mismatch")
    if sha256(parent_blend) != PARENT_BLEND_SHA256:
        raise RuntimeError("Immutable v004 blend hash mismatch")
    if not successor_blend.is_file():
        raise FileNotFoundError("Validated v005 blend is missing")

    data = json.loads(parent_transfer.read_text(encoding="utf-8"))
    successor = copy.deepcopy(data)
    successor["status"] = "DERIVED_V005_PREPARED_FOR_NATIVE_UNITY_IMPORT"
    successor["source_blend"] = successor_blend.relative_to(root).as_posix()
    successor["source_blend_sha256"] = sha256(successor_blend)
    successor["derivation"] = {
        "parent_blend": parent_blend.relative_to(root).as_posix(),
        "parent_blend_sha256": PARENT_BLEND_SHA256,
        "parent_transfer": parent_transfer.relative_to(root).as_posix(),
        "parent_transfer_sha256": PARENT_TRANSFER_SHA256,
        "scope": "SEAT_412_413_414_SIDE_PLACEMENT_ONLY",
    }
    scale_x = TARGET_WIDTH_M / ORIGINAL_WIDTH_M
    changed = []
    for item in successor["roots"]:
        identifier = item["id"]
        if identifier not in TARGETS:
            continue
        x, y, z = TARGETS[identifier]
        item["position"] = {"x": x, "y": y, "z": z}
        item["yaw_degrees"] = 90.0
        for collider in item["colliders"]:
            collider["center"]["x"] *= scale_x
            collider["size"]["x"] *= scale_x
        changed.append(identifier)
    if sorted(changed) != sorted(TARGETS):
        raise RuntimeError(f"Did not derive all three seat roots: {changed}")
    counts = successor["collider_counts"]
    expected = {
        "roots": 380,
        "colliders": 554,
        "boxes": 549,
        "static_mesh_proxies": 4,
        "capsule_proxies": 1,
        "teleport_surfaces": 28,
    }
    if counts != expected:
        raise RuntimeError(f"Transfer count contract changed: {counts}")

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(successor, indent=2) + "\n", encoding="utf-8")
    receipt = {
        "status": "PASS",
        "output": output.relative_to(root).as_posix(),
        "output_sha256": sha256(output),
        "source_blend": successor["source_blend"],
        "source_blend_sha256": successor["source_blend_sha256"],
        "parent_blend_sha256": PARENT_BLEND_SHA256,
        "changed_roots": changed,
        "counts": counts,
        "unity": "NOT_RUN",
        "quest": "NOT_RUN",
    }
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(receipt))


if __name__ == "__main__":
    main()
