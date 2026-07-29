"""Audit the dense AlwaysOne safety/baseline clip in a generated AVR Math tree.

For each curve in the binder this proves whether another Math-root child already
authors the same parameter on every possible evaluation path.  If so, a zero
baseline curve can simply be removed.  A non-zero baseline can be absorbed into
that child's guaranteed partition-of-unity cover (all leaves of a 1D tree, or
an AlwaysOne child of a Direct tree).  Both rewrites preserve the existing AAP
write/read epoch and never require a negative Direct blend weight.

The proof is deliberately conservative.  It does not assume that an arbitrary
set of runtime weights sums to one, and it does not treat Animator defaults as
per-frame writes.  A parameter with no total writer therefore remains bound,
even if its mathematical value is usually zero.
"""

from __future__ import annotations

import argparse
import functools
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from avr_congruence import Analyzer  # noqa: E402
from analyze_math_hotspots import (  # noqa: E402
    DEFAULT_CONTROLLER,
    motion_name,
    populated_float_curves,
    selected_math_root,
    semantic_group,
)

ALWAYS_ONE = "__YUCP_AVR_ONE"
DEFAULT_JSON = HERE / "always_one_binder_audit.json"
DEFAULT_REPORT = HERE / "always_one_binder_audit.md"


def find_binder(analyzer: Analyzer, root: dict) -> tuple[int, int, dict]:
    matches = []
    for index, child in enumerate(root.get("m_Childs", [])):
        motion_id = int(child["m_Motion"]["fileID"])
        clip = analyzer.clips.get(motion_id)
        if (
            child.get("m_DirectBlendParameter") == ALWAYS_ONE
            and clip is not None
            and clip.get("m_Name") == f"Folded constants by {ALWAYS_ONE}"
        ):
            matches.append((index, motion_id, clip))
    if len(matches) != 1:
        raise AssertionError(f"expected one dense AlwaysOne binder, got {len(matches)}")
    return matches[0]


class GraphFacts:
    def __init__(self, analyzer: Analyzer):
        self.a = analyzer
        self.clip_bindings = {
            motion_id: {
                str(curve["attribute"])
                for curve in populated_float_curves(clip)
                if int(curve.get("classID", 0)) == 95 and not (curve.get("path") or "")
            }
            for motion_id, clip in analyzer.clips.items()
        }

    @functools.lru_cache(maxsize=None)
    def must_bind(self, motion_id: int, target: str) -> bool:
        """True when every evaluation path authors target at least once."""
        clip = self.a.clips.get(motion_id)
        if clip is not None:
            return target in self.clip_bindings[motion_id]
        tree = self.a.trees.get(motion_id)
        if tree is None:
            return False
        children = tree.get("m_Childs", [])
        if not children:
            return False
        blend_type = int(tree.get("m_BlendType", 0))
        if blend_type == 0:
            # A 1D tree selects one endpoint or an interpolation of two.  Every
            # leaf must bind the target for the output union to be total.
            return all(
                self.must_bind(int(child["m_Motion"]["fileID"]), target)
                for child in children
            )
        if blend_type == 4 and not tree.get("m_NormalizedBlendValues", 0):
            # Arbitrary Direct weights may all be zero.  Only an AlwaysOne child
            # provides a static total cover without an extra runtime invariant.
            return any(
                child.get("m_DirectBlendParameter") == ALWAYS_ONE
                and self.must_bind(int(child["m_Motion"]["fileID"]), target)
                for child in children
            )
        return False

    def reads(self, motion_id: int) -> list[tuple[str, str]]:
        output: list[tuple[str, str]] = []

        def walk(current: int) -> None:
            if current in self.a.clips:
                return
            tree = self.a.trees.get(current)
            if tree is None:
                return
            blend_type = int(tree.get("m_BlendType", 0))
            if blend_type == 4:
                for child in tree.get("m_Childs", []):
                    output.append((str(child.get("m_DirectBlendParameter", "")), "direct"))
                    walk(int(child["m_Motion"]["fileID"]))
            else:
                output.append((str(tree.get("m_BlendParameter", "")), "1d"))
                for child in tree.get("m_Childs", []):
                    walk(int(child["m_Motion"]["fileID"]))

        walk(motion_id)
        return output

    def writes(self, motion_id: int) -> set[str]:
        output: set[str] = set()
        seen: set[int] = set()

        def walk(current: int) -> None:
            if current in seen:
                return
            seen.add(current)
            clip = self.a.clips.get(current)
            if clip is not None:
                output.update(self.clip_bindings[current])
                return
            tree = self.a.trees.get(current)
            if tree is None:
                return
            for child in tree.get("m_Childs", []):
                walk(int(child["m_Motion"]["fileID"]))

        walk(motion_id)
        return output


def audit(controller: Path) -> dict:
    analyzer = Analyzer(str(controller))
    root, _ = selected_math_root(analyzer)
    facts = GraphFacts(analyzer)
    binder_index, binder_motion, binder = find_binder(analyzer, root)
    children = root.get("m_Childs", [])

    reader_sites: dict[str, list[dict]] = defaultdict(list)
    producer_sites: dict[str, list[dict]] = defaultdict(list)
    for child_index, child in enumerate(children):
        motion_id = int(child["m_Motion"]["fileID"])
        root_weight = str(child.get("m_DirectBlendParameter", ""))
        reader_sites[root_weight].append({
            "child": child_index,
            "kind": "direct-root",
            "motion": motion_name(analyzer, motion_id),
        })
        for parameter, kind in facts.reads(motion_id):
            reader_sites[parameter].append({
                "child": child_index,
                "kind": kind,
                "motion": motion_name(analyzer, motion_id),
            })
        for parameter in facts.writes(motion_id):
            producer_sites[parameter].append({
                "child": child_index,
                "weight": root_weight,
                "motion": motion_name(analyzer, motion_id),
            })

    rows = []
    for curve in populated_float_curves(binder):
        target = str(curve["attribute"])
        baseline = float(curve["curve"]["m_Curve"][0]["value"])
        covers = []
        for child_index, child in enumerate(children):
            if child_index == binder_index:
                continue
            if child.get("m_DirectBlendParameter") != ALWAYS_ONE:
                continue
            motion_id = int(child["m_Motion"]["fileID"])
            if facts.must_bind(motion_id, target):
                covers.append({
                    "child": child_index,
                    "motion": motion_name(analyzer, motion_id),
                })
        readers = reader_sites.get(target, [])
        reader_kinds = sorted({site["kind"] for site in readers})
        removable = bool(covers)
        if not readers:
            reader_class = "no-math-readers"
        elif "direct" in reader_kinds or "direct-root" in reader_kinds:
            reader_class = "direct-and-1d" if "1d" in reader_kinds else "direct"
        else:
            reader_class = "1d"
        rows.append({
            "parameter": target,
            "semantic_group": semantic_group(target),
            "baseline": baseline,
            "zero_baseline": baseline == 0.0,
            "exactly_removable": removable,
            "proof": (
                "drop-zero-curve" if removable and baseline == 0.0
                else "absorb-baseline-into-total-cover" if removable
                else "no-total-writer; per-frame rebind still required"
            ),
            "total_covers": covers,
            "reader_class": reader_class,
            "reader_site_count": len(readers),
            "reader_sites": readers,
            "producer_site_count": len(producer_sites.get(target, [])),
            "producer_sites": producer_sites.get(target, []),
        })

    groups = {}
    for group in sorted({row["semantic_group"] for row in rows}):
        items = [row for row in rows if row["semantic_group"] == group]
        removable = [row for row in items if row["exactly_removable"]]
        retained = [row for row in items if not row["exactly_removable"]]
        groups[group] = {
            "curves": len(items),
            "exactly_removable_curves": len(removable),
            "retained_curves": len(retained),
            "removable_nonzero_baselines": sum(not row["zero_baseline"] for row in removable),
            "retained_nonzero_baselines": sum(not row["zero_baseline"] for row in retained),
            "removable_reader_sites": sum(row["reader_site_count"] for row in removable),
            "retained_reader_sites": sum(row["reader_site_count"] for row in retained),
        }

    removable = [row for row in rows if row["exactly_removable"]]
    retained = [row for row in rows if not row["exactly_removable"]]
    reader_classes = Counter(row["reader_class"] for row in retained)
    return {
        "controller": str(controller),
        "binder": {
            "math_child": binder_index,
            "motion_id": binder_motion,
            "name": binder.get("m_Name"),
            "curves": len(rows),
            "zero_baselines": sum(row["zero_baseline"] for row in rows),
            "nonzero_baselines": sum(not row["zero_baseline"] for row in rows),
        },
        "verdict": {
            "exactly_removable_curves": len(removable),
            "removable_zero_curves": sum(row["zero_baseline"] for row in removable),
            "removable_nonzero_curves": sum(not row["zero_baseline"] for row in removable),
            "retained_curves": len(retained),
            "retained_zero_curves": sum(row["zero_baseline"] for row in retained),
            "retained_nonzero_curves": sum(not row["zero_baseline"] for row in retained),
            "removable_reader_sites_unchanged": sum(
                row["reader_site_count"] for row in removable
            ),
            "removable_distinct_reader_children_unchanged": len({
                site["child"] for row in removable for site in row["reader_sites"]
            }),
            "retained_reader_sites": sum(row["reader_site_count"] for row in retained),
            "retained_distinct_reader_children": len({
                site["child"] for row in retained for site in row["reader_sites"]
            }),
            "retained_reader_classes": dict(reader_classes),
            "parameters_with_no_other_producer": sum(
                row["producer_site_count"] == 1 for row in rows
            ),
        },
        "semantic_groups": groups,
        "parameters": rows,
        "proof_scope": {
            "allowed": [
                "remove a zero binder when another AlwaysOne subtree binds the parameter on every path",
                "absorb a non-zero baseline into an existing total 1D/AlwaysOne cover",
                "change constant curve values inside the same AAP write stage",
            ],
            "not_assumed": [
                "Animator defaults rebind an AAP every frame",
                "arbitrary Direct weights form a partition of unity",
                "stale values are harmless behind a correlated gate",
                "negative values are legal Direct blend weights",
                "a downstream reader may be advanced to its producer's current epoch",
            ],
        },
    }


def markdown(result: dict) -> str:
    binder = result["binder"]
    verdict = result["verdict"]
    lines = [
        "# AVR AlwaysOne binder audit",
        "",
        f"Controller: `{result['controller']}`",
        "",
        (
            f"The Math child `{binder['name']}` contains **{binder['curves']}** active "
            f"parameter curves: {binder['zero_baselines']} zero baselines and "
            f"{binder['nonzero_baselines']} non-zero affine offsets."
        ),
        "",
        (
            f"A conservative same-epoch proof finds **{verdict['exactly_removable_curves']} "
            f"curves removable exactly** ({verdict['removable_zero_curves']} zero, "
            f"{verdict['removable_nonzero_curves']} non-zero) and "
            f"**{verdict['retained_curves']} curves that must remain** in the current topology."
        ),
        "",
        "## By semantic group",
        "",
        "| Group | Binder curves | Exact removal | Retain | Non-zero removed/retained |",
        "|---|---:|---:|---:|---:|",
    ]
    for group, item in result["semantic_groups"].items():
        lines.append(
            f"| {group} | {item['curves']} | {item['exactly_removable_curves']} | "
            f"{item['retained_curves']} | {item['removable_nonzero_baselines']}/"
            f"{item['retained_nonzero_baselines']} |"
        )
    lines.extend([
        "",
        "## Why the exact removal is safe",
        "",
        (
            "For a target `y = c + dynamic`, a zero `c` curve is redundant when another "
            "AlwaysOne Math subtree authors `y` on every possible path. For non-zero `c`, "
            "add `c` to that subtree's existing 1D leaves (or its guaranteed AlwaysOne "
            "child). The same parameter is written in the same Math evaluation and every "
            "reader still observes the same previous-frame AAP value."
        ),
        "",
        (
            f"The removable parameters currently feed {verdict['removable_reader_sites_unchanged']} "
            f"reader sites across {verdict['removable_distinct_reader_children_unchanged']} "
            "Math children. None of those readers or their thresholds/weights needs to change."
        ),
        "",
        "## Why the rest cannot be shifted away",
        "",
        (
            f"The remaining {verdict['retained_curves']} parameters have no other total writer. "
            "If their conditional producer becomes inactive, an unbound AAP may retain its "
            "previous value; an Animator default is not a per-frame zero write. Merely changing "
            "coordinates moves the baseline but does not provide that missing write."
        ),
        "",
        (
            f"They feed {verdict['retained_reader_sites']} reader sites across "
            f"{verdict['retained_distinct_reader_children']} Math children. Reader classes: "
            + ", ".join(
                f"{name}={count} parameters"
                for name, count in sorted(verdict["retained_reader_classes"].items())
            )
            + "."
        ),
        "",
        (
            "In particular, parameters used as Direct weights cannot generally be shifted "
            "around zero: the shifted coordinate may be negative, which is not a usable Direct "
            "blend weight. The audit therefore does not claim savings from signed weights, "
            "correlated gates, or moving consumers across the Math AAP epoch."
        ),
        "",
        "## Non-zero offsets that can be absorbed",
        "",
        "| Parameter | Baseline | Total writer cover | Reader sites |",
        "|---|---:|---|---:|",
    ])
    for row in result["parameters"]:
        if row["exactly_removable"] and not row["zero_baseline"]:
            cover = "; ".join(
                f"#{item['child']} {item['motion']}" for item in row["total_covers"]
            )
            lines.append(
                f"| `{row['parameter']}` | {row['baseline']:.6g} | {cover} | "
                f"{row['reader_site_count']} |"
            )
    lines.extend([
        "",
        "## Exact-removal parameter list",
        "",
        "| Group | Parameter | Baseline | Cover child | Readers |",
        "|---|---|---:|---|---:|",
    ])
    for row in result["parameters"]:
        if not row["exactly_removable"]:
            continue
        cover = row["total_covers"][0]
        lines.append(
            f"| {row['semantic_group']} | `{row['parameter']}` | "
            f"{row['baseline']:.6g} | #{cover['child']} {cover['motion']} | "
            f"{row['reader_site_count']} |"
        )
    lines.extend([
        "",
        "The JSON artifact contains every retained parameter, producer, reader, and proof result.",
    ])
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--controller", type=Path, default=DEFAULT_CONTROLLER)
    parser.add_argument("--json", type=Path, default=DEFAULT_JSON)
    parser.add_argument("--report", type=Path, default=DEFAULT_REPORT)
    args = parser.parse_args()
    result = audit(args.controller)
    args.json.write_text(json.dumps(result, indent=2), encoding="utf-8")
    args.report.write_text(markdown(result), encoding="utf-8")
    print(json.dumps(result["verdict"], indent=2))
    print(f"wrote {args.json}")
    print(f"wrote {args.report}")


if __name__ == "__main__":
    main()
