"""Causal replay evaluator for generated AVR AnimatorControllers.

Implements the synchronous evaluation model the generator is written against:
layers evaluate in order; within a layer every curve read sees parameter
values from before the layer's writes; a layer's writes commit together
(one epoch). Direct trees sum weight*value with weights clamped at zero and
no normalization; Simple1D trees interpolate piecewise-linearly between
threshold knots with end clamping. All generated curves are constant, which
the loader asserts.

Replays two controllers over identical randomized input streams and reports
the maximum absolute difference over all comparable outputs: every written
non-internal Animator parameter and every physical (non-Animator) float
curve target. Zero means the optimization is observationally exact.
"""

import sys
import numpy as np
from collections import defaultdict
from avr_congruence import Analyzer, INTERNAL_PREFIX

FPS_VALUES = (15, 30, 60, 90, 144)


class Node:
    __slots__ = ("kind", "param", "thresholds", "children", "weights",
                 "writes", "physical")

    def __init__(self, kind):
        self.kind = kind          # "direct" | "1d" | "clip"
        self.param = None
        self.thresholds = None
        self.children = None      # list of Node
        self.weights = None       # direct: list of param names
        self.writes = None        # clip: list of (param, value)
        self.physical = None      # clip: list of (target, value)


class LayerProgram:
    def __init__(self, root, name):
        self.root = root
        self.name = name


class ControllerProgram:
    def __init__(self, path):
        a = Analyzer(path)
        self.analyzer = a
        self.defaults = {}
        for p in a.controller["m_AnimatorParameters"]:
            self.defaults[p["m_Name"]] = float(
                p.get("m_DefaultFloat", 0) or 0)
        self.layers = []
        self.written = set()
        self.reads = set()
        skipped = []
        for layer in a.controller["m_AnimatorLayers"]:
            sm_id = layer["m_StateMachine"]["fileID"]
            machine = a.machines.get(sm_id)
            name = layer.get("m_Name", "?")
            states = machine.get("m_ChildStates", []) if machine else []
            single = (machine is not None and len(states) == 1 and
                      not machine.get("m_ChildStateMachines"))
            if single:
                state = a.states[states[0]["m_State"]["fileID"]]
                transitions = state.get("m_Transitions", [])
                motion = state.get("m_Motion", {}).get("fileID", 0)
                if not transitions and motion and \
                        not state.get("m_TimeParameterActive", 0):
                    written_before = set(self.written)
                    reads_before = set(self.reads)
                    try:
                        root = self.compile_motion(motion)
                    except AssertionError:
                        self.written = written_before
                        self.reads = reads_before
                        # State-time-driven layers (e.g. the continuous time
                        # ramp) are not synchronous dataflow; their outputs
                        # are driven externally and identically for A and B.
                        skipped.append(name)
                        continue
                    self.layers.append(LayerProgram(root, name))
                    continue
            skipped.append(name)
        self.skipped = skipped
        # External inputs: read by compiled layers but never written by them.
        self.inputs = sorted(p for p in self.reads
                             if p not in self.written and p in self.defaults)

    def compile_motion(self, motion_id):
        a = self.analyzer
        clip = a.clips.get(motion_id)
        if clip is not None:
            node = Node("clip")
            node.writes, node.physical = [], []
            for fc in clip.get("m_FloatCurves") or []:
                keys = fc["curve"]["m_Curve"]
                if not keys:
                    continue
                v0 = keys[0]["value"]
                assert all(k["value"] == v0 for k in keys), \
                    f"non-constant curve in clip {clip.get('m_Name')}"
                target = fc["attribute"]
                if fc.get("classID") == 95 and not (fc.get("path") or ""):
                    node.writes.append((target, float(v0)))
                    self.written.add(target)
                else:
                    node.physical.append(
                        ((fc.get("path") or "", target), float(v0)))
            return node
        tree = a.trees[motion_id]
        blend_type = tree.get("m_BlendType", 0)
        children = tree.get("m_Childs", [])
        if blend_type == 4:
            assert not tree.get("m_NormalizedBlendValues", 0), \
                "normalized Direct tree unsupported"
            node = Node("direct")
            node.weights = [c.get("m_DirectBlendParameter", "")
                            for c in children]
            for w in node.weights:
                self.reads.add(w)
            node.children = [self.compile_motion(c["m_Motion"]["fileID"])
                             for c in children]
            return node
        assert blend_type == 0, f"unsupported blend type {blend_type}"
        node = Node("1d")
        node.param = tree.get("m_BlendParameter", "")
        self.reads.add(node.param)
        node.thresholds = np.array(
            [float(c.get("m_Threshold", 0)) for c in children])
        assert np.all(np.diff(node.thresholds) >= 0), "unsorted thresholds"
        node.children = [self.compile_motion(c["m_Motion"]["fileID"])
                         for c in children]
        return node

    def evaluate(self, node, weight, params, writes, physical):
        if weight == 0.0:
            return
        if node.kind == "clip":
            for target, value in node.writes:
                writes[target] = writes.get(target, 0.0) + weight * value
            for target, value in node.physical:
                physical[target] = physical.get(target, 0.0) + weight * value
            return
        if node.kind == "direct":
            for child, wp in zip(node.children, node.weights):
                w = params.get(wp, 0.0)
                if w > 0.0:
                    self.evaluate(child, weight * w, params, writes, physical)
            return
        x = params.get(node.param, 0.0)
        t = node.thresholds
        n = len(t)
        if n == 1 or x <= t[0]:
            self.evaluate(node.children[0], weight, params, writes, physical)
            return
        if x >= t[-1]:
            self.evaluate(node.children[-1], weight, params, writes, physical)
            return
        hi = int(np.searchsorted(t, x, side="right"))
        lo = hi - 1
        span = t[hi] - t[lo]
        frac = 0.0 if span == 0 else (x - t[lo]) / span
        if frac < 1.0:
            self.evaluate(node.children[lo], weight * (1.0 - frac),
                          params, writes, physical)
        if frac > 0.0:
            self.evaluate(node.children[hi], weight * frac,
                          params, writes, physical)

    def step(self, params):
        """Advance one frame; returns physical outputs for this frame."""
        physical = {}
        for layer in self.layers:
            writes = {}
            self.evaluate(layer.root, 1.0, params, writes, physical)
            params.update(writes)
        return physical


def drive_inputs(program, rng, frames, dt):
    """Randomized piecewise-constant/linear input streams (seeded)."""
    streams = {}
    for name in program.inputs:
        lower, upper = 0.0, 1.0
        if "Viseme/Index" in name or name.endswith("VisemeIdx"):
            lower, upper = 0.0, 14.0
        if "Tongue" in name and INTERNAL_PREFIX not in name:
            lower = -1.0
        hold = rng.integers(3, 40)
        values = []
        current = float(rng.uniform(lower, upper))
        step_kind = rng.random()
        for frame in range(frames):
            if frame % hold == 0:
                if step_kind < 0.5:
                    current = float(rng.uniform(lower, upper))
                else:
                    current = float(np.clip(
                        current + rng.normal(0, 0.25 * (upper - lower)),
                        lower, upper))
                if "Index" in name:
                    current = float(rng.integers(0, 15))
            values.append(current)
        streams[name] = values
    for name in list(streams):
        if "DeltaTime" in name or "FrameTime" in name:
            streams[name] = [dt] * frames
    return streams


def replay(path_a, path_b, traces=8, frames=400, seed=730241):
    a, b = ControllerProgram(path_a), ControllerProgram(path_b)
    outputs_a = {p for p in a.written
                 if not p.startswith(INTERNAL_PREFIX)}
    outputs_b = {p for p in b.written
                 if not p.startswith(INTERNAL_PREFIX)}
    assert outputs_a == outputs_b, (
        "public output sets differ: " +
        str(sorted(outputs_a ^ outputs_b)[:10]))
    inputs = sorted(set(a.inputs) | set(b.inputs))
    print(f"A: {path_a}\n   layers={len(a.layers)} skipped={a.skipped}")
    print(f"B: {path_b}\n   layers={len(b.layers)} skipped={b.skipped}")
    print(f"inputs driven: {len(inputs)}  public outputs: {len(outputs_a)}")

    worst = 0.0
    worst_at = None
    samples = 0
    diffs = []            # per-frame max |A-B| across all outputs
    velocity_diffs = []   # per-frame max |dA-dB| across all outputs
    rng = np.random.default_rng(seed)
    for trace in range(traces):
        for fps in FPS_VALUES:
            dt = 1.0 / fps
            pa = dict(a.defaults)
            pb = dict(b.defaults)
            stream_rng = np.random.default_rng(rng.integers(0, 2**63))
            streams = drive_inputs(a, stream_rng, frames, dt)
            for name in inputs:
                if name not in streams:
                    streams[name] = [a.defaults.get(name, 0.0)] * frames
            prev_a, prev_b = {}, {}
            for frame in range(frames):
                for name in inputs:
                    pa[name] = streams[name][frame]
                    pb[name] = streams[name][frame]
                phys_a = a.step(pa)
                phys_b = b.step(pb)
                samples += 1
                frame_diff = 0.0
                frame_vel = 0.0
                for key in set(phys_a) | set(phys_b):
                    va = phys_a.get(key, 0.0)
                    vb = phys_b.get(key, 0.0)
                    diff = abs(va - vb)
                    if diff > frame_diff:
                        frame_diff = diff
                    if diff > worst:
                        worst, worst_at = diff, ("physical", key, trace, fps,
                                                 frame)
                    if key in prev_a:
                        vel = abs((va - prev_a[key]) - (vb - prev_b[key]))
                        if vel > frame_vel:
                            frame_vel = vel
                    prev_a[key] = va
                    prev_b[key] = vb
                for name in outputs_a:
                    va = pa.get(name, 0.0)
                    vb = pb.get(name, 0.0)
                    diff = abs(va - vb)
                    if diff > frame_diff:
                        frame_diff = diff
                    if diff > worst:
                        worst, worst_at = diff, ("param", name, trace, fps,
                                                 frame)
                    if name in prev_a:
                        vel = abs((va - prev_a[name]) - (vb - prev_b[name]))
                        if vel > frame_vel:
                            frame_vel = vel
                    prev_a[name] = va
                    prev_b[name] = vb
                diffs.append(frame_diff)
                velocity_diffs.append(frame_vel)
    diffs = np.asarray(diffs)
    velocity_diffs = np.asarray(velocity_diffs)
    print(f"frames replayed: {samples} "
          f"({samples * 2} controller evaluations)")
    print(f"max |A-B| over public params + physical outputs: {worst}")
    print(f"per-frame max-diff stats: rms={np.sqrt(np.mean(diffs**2)):.6g} "
          f"p99={np.quantile(diffs, 0.99):.6g} max={diffs.max():.6g} "
          f"velocityRms={np.sqrt(np.mean(velocity_diffs**2)):.6g}")
    if worst_at is not None and worst > 0:
        print("worst at:", worst_at)
    return worst


if __name__ == "__main__":
    result = replay(sys.argv[1], sys.argv[2],
                    traces=int(sys.argv[3]) if len(sys.argv) > 3 else 8,
                    frames=int(sys.argv[4]) if len(sys.argv) > 4 else 400)
    sys.exit(0 if result == 0.0 else 1)
