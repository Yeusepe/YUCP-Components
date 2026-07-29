"""Offline congruence (GVN) analysis of a generated AVR AnimatorController.

Partitions internal animator parameters into classes that provably carry the
same value on every frame: start from an optimistic partition keyed by
parameter default value, then refine by write-site signatures until fixpoint
(Alpern-Wegman-Zadeck partition refinement on the synchronous dataflow graph).

A write-site signature captures, for one float curve writing parameter P:
  (layer, state, full blend-tree context chain from the state root motion,
   curve keyframes, clip timing settings)
where every parameter referenced in the context (1D blend parameter, direct
blend weight) is mapped through the current partition, so equivalence is a
congruence: P == Q iff their complete multisets of write sites are equal
modulo the equivalence itself.
"""

import re
import sys
import yaml
from collections import defaultdict

try:
    Loader = yaml.CSafeLoader
except AttributeError:
    Loader = yaml.SafeLoader

INTERNAL_PREFIX = "YUCP/AdvancedViseme/_Internal/"

DOC_RE = re.compile(r"^--- !u!(\d+) &(-?\d+)", re.M)


def parse_documents(path):
    text = open(path, encoding="utf-8").read()
    docs = []
    matches = list(DOC_RE.finditer(text))
    for i, m in enumerate(matches):
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        body = text[m.end():end]
        docs.append((int(m.group(1)), int(m.group(2)), body))
    return docs


def load(path):
    clips, trees, states, machines = {}, {}, {}, {}
    controller = None
    for class_id, file_id, body in parse_documents(path):
        if class_id not in (74, 206, 1102, 1107, 91):
            continue
        data = yaml.load(body, Loader=Loader)
        if class_id == 74:
            clips[file_id] = data["AnimationClip"]
        elif class_id == 206:
            trees[file_id] = data["BlendTree"]
        elif class_id == 1102:
            states[file_id] = data["AnimatorState"]
        elif class_id == 1107:
            machines[file_id] = data["AnimatorStateMachine"]
        elif class_id == 91:
            controller = data["AnimatorController"]
    return controller, clips, trees, states, machines


def curve_signature(curve_entry):
    keys = tuple(
        (k["time"], k["value"], k.get("inSlope", 0), k.get("outSlope", 0),
         k.get("weightedMode", 0), k.get("inWeight", 0), k.get("outWeight", 0))
        for k in curve_entry["curve"]["m_Curve"])
    return (keys, curve_entry["curve"].get("m_PreInfinity"),
            curve_entry["curve"].get("m_PostInfinity"))


class Analyzer:
    def __init__(self, path):
        self.controller, self.clips, self.trees, self.states, self.machines = \
            load(path)
        self.defaults = {}
        for p in self.controller["m_AnimatorParameters"]:
            self.defaults[p["m_Name"]] = (
                p["m_Type"], p.get("m_DefaultFloat", 0),
                p.get("m_DefaultInt", 0), p.get("m_DefaultBool", 0))
        # write_sites[param] = list of (context_template, curve_sig)
        # context_template: tuple of steps; parameter references appear as
        # ("p", name) so they can be mapped through the partition later.
        self.write_sites = defaultdict(list)
        self.tree_of_site = defaultdict(list)  # param -> list of tree ids on path
        self.collect_sites()

    def collect_sites(self):
        for layer_index, layer in enumerate(self.controller["m_AnimatorLayers"]):
            sm_id = layer["m_StateMachine"]["fileID"]
            if sm_id == 0:
                continue
            self.walk_machine(layer_index, sm_id)

    def walk_machine(self, layer_index, machine_id):
        machine = self.machines.get(machine_id)
        if machine is None:
            return
        for child in machine.get("m_ChildStates", []):
            sid = child["m_State"]["fileID"]
            state = self.states.get(sid)
            if state is None:
                continue
            motion = state.get("m_Motion", {}).get("fileID", 0)
            if motion:
                tag = ("site", layer_index, sid,
                       state.get("m_Speed", 1),
                       state.get("m_TimeParameterActive", 0))
                self.walk_motion(motion, tag, (), [])
        for child in machine.get("m_ChildStateMachines", []):
            self.walk_machine(layer_index, child["m_StateMachine"]["fileID"])

    def walk_motion(self, motion_id, tag, context, tree_path):
        clip = self.clips.get(motion_id)
        if clip is not None:
            settings = clip.get("m_AnimationClipSettings", {})
            clip_sig = (settings.get("m_StartTime"), settings.get("m_StopTime"),
                        settings.get("m_LoopTime"), clip.get("m_SampleRate"))
            for fc in clip.get("m_FloatCurves") or []:
                if fc.get("classID") != 95 or (fc.get("path") or "") != "":
                    continue
                attr = fc["attribute"]
                self.write_sites[attr].append(
                    (tag, context, clip_sig, curve_signature(fc)))
                self.tree_of_site[attr].append(tuple(tree_path))
            return
        tree = self.trees.get(motion_id)
        if tree is None:
            return
        children = tree.get("m_Childs", [])
        blend_type = tree.get("m_BlendType", 0)
        thresholds = tuple(c.get("m_Threshold", 0) for c in children)
        positions = tuple(
            (c.get("m_Position", {}).get("x", 0),
             c.get("m_Position", {}).get("y", 0)) for c in children)
        normalized = tree.get("m_NormalizedBlendValues", 0)
        for index, child in enumerate(children):
            # Non-normalized Direct children sum commutatively: neither the
            # sibling index nor the sibling count is part of one child's
            # weight function. Threshold/positional trees keep both.
            direct = blend_type == 4 and not normalized
            step = ("tree", blend_type, normalized,
                    ("p", tree.get("m_BlendParameter", "")),
                    ("p", tree.get("m_BlendParameterY", "")),
                    thresholds if blend_type != 4 else None,
                    positions if blend_type in (1, 2, 3) else None,
                    None if direct else len(children),
                    None if direct else index,
                    ("p", child.get("m_DirectBlendParameter", ""))
                    if blend_type == 4 else None,
                    child.get("m_TimeScale", 1),
                    child.get("m_Mirror", 0),
                    child.get("m_CycleOffset", 0))
            self.walk_motion(child["m_Motion"]["fileID"], tag,
                             context + (step,), tree_path + [motion_id])

    def refine(self):
        written = sorted(self.write_sites)
        internal = [p for p in written if p.startswith(INTERNAL_PREFIX)]
        # class id per param; external / non-candidate params map to themselves.
        cls = {}
        for p in internal:
            cls[p] = ("c0", self.defaults.get(p))
        rounds = 0
        while True:
            rounds += 1
            def map_param(name):
                return cls.get(name, ("x", name))

            def map_ctx(entry):
                tag, context, clip_sig, csig = entry
                mapped = tuple(
                    tuple(("p", map_param(item[1])) if isinstance(item, tuple)
                          and len(item) == 2 and item[0] == "p" else item
                          for item in step)
                    for step in context)
                return (tag, mapped, clip_sig, csig)

            sigs = {}
            for p in internal:
                sites = tuple(sorted(
                    (repr(map_ctx(e)) for e in self.write_sites[p])))
                sigs[p] = (cls[p], sites)
            groups = defaultdict(list)
            for p in internal:
                groups[sigs[p]].append(p)
            new_cls = {}
            for i, (_, members) in enumerate(sorted(
                    groups.items(), key=lambda kv: kv[1][0])):
                for p in members:
                    new_cls[p] = ("c", i)
            if new_cls == cls:
                break
            cls = new_cls
        classes = defaultdict(list)
        for p in internal:
            classes[cls[p]].append(p)
        return {k: sorted(v) for k, v in classes.items() if len(v) > 1}, rounds


def main():
    path = sys.argv[1]
    a = Analyzer(path)
    dup_classes, rounds = a.refine()
    total_params = sum(len(v) - 1 for v in dup_classes.values())
    total_curves = sum(
        (len(v) - 1) * len(a.write_sites[v[0]]) for v in dup_classes.values())
    all_internal = [p for p in a.write_sites if p.startswith(INTERNAL_PREFIX)]
    print(f"controller: {path}")
    print(f"written params: {len(a.write_sites)}  "
          f"internal written: {len(all_internal)}")
    print(f"refinement rounds: {rounds}")
    print(f"congruence classes with duplicates: {len(dup_classes)}")
    print(f"removable duplicate parameters: {total_params}")
    print(f"removable duplicate write curves: {total_curves}")
    print()
    for key, members in sorted(dup_classes.items(),
                               key=lambda kv: -len(kv[1])):
        sites = len(a.write_sites[members[0]])
        print(f"[{len(members)} members x {sites} sites] "
              + " | ".join(m[len(INTERNAL_PREFIX):] for m in members))


if __name__ == "__main__":
    main()
