using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace YUCP.Components.Editor.Tests
{
    public sealed class ParameterCompressorAnimatorBuilderTests
    {
        private const string Prefix = "YUCP/TestCompressor";
        private const float Frame = 1f / 60f;

        /// <summary>A change preempts the refresh on the wire, so it only waits for VRChat's
        /// next sync, plus half a hold when the remote is already reading that value.</summary>
        [Test]
        public void ChangesReachTheRemoteWithinOneSync()
        {
            var entries = Entries(out var pageBits);
            var controller = Build(entries, pageBits);
            using (var wearer = new Client(controller, true))
            using (var remote = new Client(controller, false))
            {
                Run(wearer, remote, 30f * ParameterCompressorAnimatorBuilder.TickSeconds);
                AssertSynced(entries, wearer, remote);

                var random = new System.Random(7);
                var latencies = new List<float>();
                for (var change = 0; change < 60; change++)
                {
                    var entry = entries[random.Next(entries.Count)];
                    wearer.Set(entry, RandomValue(entry, random));
                    var seconds = 0f;
                    while (!Matches(entry, wearer, remote) && seconds < 2f)
                        seconds += Run(wearer, remote, Frame);
                    Assert.That(seconds, Is.LessThanOrEqualTo(
                        ParameterCompressorAnimatorBuilder.HoldSeconds +
                        ParameterCompressorAnimatorBuilder.ReadSeconds + 4f * Frame), entry.name);
                    AssertSynced(entries, wearer, remote);
                    latencies.Add(seconds);
                    Run(wearer, remote, 0.15f + (float)random.NextDouble() * 0.3f);
                }
                Debug.Log("[YUCP Parameter Compressor] Change latency mean " +
                          latencies.Average().ToString("0.000") + " s, max " +
                          latencies.Max().ToString("0.000") + " s.");
            }
            UnityEngine.Object.DestroyImmediate(controller);
        }

        [Test]
        public void LateJoinerConvergesWithinOneRefreshCycle()
        {
            var entries = Entries(out var pageBits);
            var controller = Build(entries, pageBits, out var result);
            using (var wearer = new Client(controller, true))
            {
                var random = new System.Random(11);
                foreach (var entry in entries) wearer.Set(entry, RandomValue(entry, random));
                Run(wearer, null, 3f);
                using (var remote = new Client(controller, false))
                {
                    Run(wearer, remote, result.estimatedFullRefreshSeconds + 0.5f);
                    AssertSynced(entries, wearer, remote);
                }
            }
            UnityEngine.Object.DestroyImmediate(controller);
        }

        /// <summary>Each tick is a whole message, so a lost one only delays that value.</summary>
        [Test]
        public void DroppedTicksHealOnTheNextRefresh()
        {
            var entries = Entries(out var pageBits);
            var controller = Build(entries, pageBits, out var result);
            using (var wearer = new Client(controller, true))
            using (var remote = new Client(controller, false))
            {
                var random = new System.Random(3);
                for (var change = 0; change < 20; change++)
                {
                    foreach (var entry in entries.Where(_ => random.Next(3) == 0))
                        wearer.Set(entry, RandomValue(entry, random));
                    Run(wearer, remote, 0.2f, random);
                }
                Run(wearer, remote, 3f * result.estimatedFullRefreshSeconds, random);
                AssertSynced(entries, wearer, remote);
            }
            UnityEngine.Object.DestroyImmediate(controller);
        }

        [Test]
        public void CarrierIsOneIntCodeAndPageBools()
        {
            var entries = Entries(out var pageBits);
            var controller = Build(entries, pageBits, out var result);
            var types = controller.parameters.ToDictionary(parameter => parameter.name, parameter => parameter.type);

            Assert.That(result.carrierParameters, Is.EqualTo(new[] { Prefix + "/_Bus/Code" }
                .Concat(Enumerable.Range(0, pageBits).Select(bit => Prefix + "/_Bus/Page" + bit))));
            Assert.That(types[Prefix + "/_Bus/Code"], Is.EqualTo(AnimatorControllerParameterType.Int));
            Assert.That(result.carrierParameters.Skip(1), Has.All.Matches<string>(name =>
                types[name] == AnimatorControllerParameterType.Bool));
            Assert.That(result.generatedStates, Is.LessThanOrEqualTo(3 + 6 * entries.Count));
            UnityEngine.Object.DestroyImmediate(controller);
        }

        /// <summary>Radials, signed Floats, Bools and a small Int, laid out by the planner.</summary>
        private static List<ParameterCompressorAnimatorBuilder.Entry> Entries(out int pageBits)
        {
            var entries = new List<ParameterCompressorAnimatorBuilder.Entry>();
            void Add(string name, AnimatorControllerParameterType type, int levels, float minimum, float maximum) =>
                entries.Add(new ParameterCompressorAnimatorBuilder.Entry
                {
                    name = name, type = type, levels = levels, minimum = minimum, maximum = maximum
                });
            for (var index = 0; index < 5; index++)
                Add("Radial" + index, AnimatorControllerParameterType.Float, 128, 0f, 1f);
            for (var index = 0; index < 2; index++)
                Add("Signed" + index, AnimatorControllerParameterType.Float, 255, -1f, 1f);
            for (var index = 0; index < 4; index++)
                Add("Toggle" + index, AnimatorControllerParameterType.Bool, 2, 0f, 1f);
            Add("Outfit", AnimatorControllerParameterType.Int, 6, 0f, 5f);

            var plan = ParameterCompressionPlanner.CreatePlan(0, 0, entries.Select(entry =>
                new ParameterCompressionCandidate(entry.name, entry.levels, 0, 8, true)));
            foreach (var allocation in plan.Allocations)
            {
                var entry = entries.Single(item => item.name == allocation.Name);
                entry.page = allocation.Page;
                entry.offset = allocation.Offset;
            }
            pageBits = plan.PageBits;
            return entries;
        }

        private static AnimatorController Build(
            IReadOnlyList<ParameterCompressorAnimatorBuilder.Entry> entries, int pageBits) =>
            Build(entries, pageBits, out _);

        private static AnimatorController Build(
            IReadOnlyList<ParameterCompressorAnimatorBuilder.Entry> entries,
            int pageBits,
            out ParameterCompressorAnimatorBuilder.Result result)
        {
            var controller = new AnimatorController { name = "Compressor Test" };
            result = ParameterCompressorAnimatorBuilder.Build(new ParameterCompressorAnimatorBuilder.Request
            {
                controller = controller,
                prefix = Prefix,
                pageBits = pageBits,
                entries = entries
            });
            return controller;
        }

        private static float RandomValue(ParameterCompressorAnimatorBuilder.Entry entry, System.Random random)
        {
            if (entry.type == AnimatorControllerParameterType.Bool) return random.Next(2);
            if (entry.type == AnimatorControllerParameterType.Int)
                return random.Next((int)entry.minimum, (int)entry.maximum + 1);
            return entry.minimum + (float)random.NextDouble() * (entry.maximum - entry.minimum);
        }

        /// <summary>What the remote should show: the value rounded to its level.</summary>
        private static bool Matches(ParameterCompressorAnimatorBuilder.Entry entry, Client wearer, Client remote)
        {
            var level = ParameterCompressorAnimatorBuilder.Level(entry, wearer.Get(entry));
            var expected = entry.type == AnimatorControllerParameterType.Bool
                ? level
                : entry.minimum + level * (entry.maximum - entry.minimum) / (entry.levels - 1);
            return Mathf.Abs(remote.Get(entry) - expected) < 1e-4f;
        }

        private static void AssertSynced(
            IEnumerable<ParameterCompressorAnimatorBuilder.Entry> entries, Client wearer, Client remote)
        {
            foreach (var entry in entries)
                Assert.That(Matches(entry, wearer, remote), Is.True,
                    entry.name + ": wearer " + wearer.Get(entry) + ", remote " + remote.Get(entry));
        }

        /// <summary>Steps both clients a frame at a time; every 0.1 s the remote gets the
        /// wearer's synced carriers, like Playable sync, losing 30% when given a random.</summary>
        private static float Run(Client wearer, Client remote, float seconds, System.Random loss = null)
        {
            var elapsed = 0f;
            for (; elapsed < seconds - 1e-6f; elapsed += Frame)
            {
                wearer.Step();
                remote?.Step();
                wearer.clock += Frame;
                if (remote == null || wearer.clock < 0.1f) continue;
                wearer.clock -= 0.1f;
                if (loss != null && loss.NextDouble() < 0.3) continue;
                foreach (var name in wearer.carriers) remote.SetRaw(name, wearer.GetRaw(name));
            }
            return elapsed;
        }

        /// <summary>
        /// One client's FX layer on a PlayableGraph. VRChat's drivers run on state
        /// enter, sequentially, localOnly ones only for the wearer; Convert Range
        /// clamps and Int writes floor, like Gesture Manager's emulation.
        /// </summary>
        private sealed class Client : IDisposable
        {
            private readonly GameObject gameObject;
            private PlayableGraph graph;
            private AnimatorControllerPlayable playable;
            private readonly bool local;
            private readonly Dictionary<string, AnimatorControllerParameterType> types;
            private readonly AnimatorControllerLayer[] layers;
            private readonly int[] active;
            internal readonly string[] carriers;
            internal float clock;

            internal Client(AnimatorController controller, bool local)
            {
                this.local = local;
                gameObject = new GameObject("Compressor client") { hideFlags = HideFlags.HideAndDontSave };
                graph = PlayableGraph.Create("Compressor client");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimatorControllerPlayable.Create(graph, controller);
                AnimationPlayableOutput.Create(graph, "FX", gameObject.AddComponent<Animator>())
                    .SetSourcePlayable(playable);
                types = controller.parameters.ToDictionary(parameter => parameter.name, parameter => parameter.type);
                carriers = types.Keys.Where(name => name.StartsWith(Prefix + "/_Bus/", StringComparison.Ordinal)).ToArray();
                layers = controller.layers;
                active = new int[layers.Length];
                graph.Evaluate(0f);
                playable.SetBool("IsLocal", local);
            }

            internal void Step()
            {
                graph.Evaluate(Frame);
                for (var index = 0; index < layers.Length; index++)
                {
                    var info = playable.IsInTransition(index)
                        ? playable.GetNextAnimatorStateInfo(index)
                        : playable.GetCurrentAnimatorStateInfo(index);
                    if (info.fullPathHash == active[index]) continue;
                    active[index] = info.fullPathHash;
                    var state = layers[index].stateMachine.states.Select(child => child.state).First(item =>
                        Animator.StringToHash(layers[index].name + "." + item.name) == info.fullPathHash);
                    foreach (var driver in state.behaviours.OfType<VRCAvatarParameterDriver>())
                        if (local || !driver.localOnly)
                            foreach (var write in driver.parameters) Apply(write);
                }
            }

            private void Apply(VRC_AvatarParameterDriver.Parameter write)
            {
                var value = write.value;
                if (write.type == VRC_AvatarParameterDriver.ChangeType.Copy)
                {
                    value = GetRaw(write.source);
                    if (write.convertRange)
                        value = write.destMin + (write.destMax - write.destMin) *
                            Mathf.Clamp01((value - write.sourceMin) / (write.sourceMax - write.sourceMin));
                }
                SetRaw(write.name, value);
            }

            internal float Get(ParameterCompressorAnimatorBuilder.Entry entry) => GetRaw(entry.name);

            internal void Set(ParameterCompressorAnimatorBuilder.Entry entry, float value) => SetRaw(entry.name, value);

            internal float GetRaw(string name)
            {
                switch (types[name])
                {
                    case AnimatorControllerParameterType.Bool: return playable.GetBool(name) ? 1f : 0f;
                    case AnimatorControllerParameterType.Int: return playable.GetInteger(name);
                    default: return playable.GetFloat(name);
                }
            }

            internal void SetRaw(string name, float value)
            {
                switch (types[name])
                {
                    case AnimatorControllerParameterType.Bool: playable.SetBool(name, value != 0f); break;
                    case AnimatorControllerParameterType.Int: playable.SetInteger(name, Mathf.FloorToInt(value)); break;
                    default: playable.SetFloat(name, value); break;
                }
            }

            public void Dispose()
            {
                graph.Destroy();
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }
    }
}
