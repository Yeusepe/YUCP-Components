using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// Builds one generated FX layer the way VRCFury's controller save and NDMF's
    /// virtual controllers do: plain objects whose arrays are each assigned once,
    /// attached to the controller asset in a single asset-editing batch. Unity's
    /// AddState, AddTransition, AddCondition, AddParameter and AddLayer record an
    /// Undo step per call, which costs several milliseconds each after a large
    /// build; a few thousand of them took minutes.
    /// </summary>
    internal sealed class GeneratedAnimatorLayer
    {
        private readonly AnimatorController controller;
        private readonly string name;
        private readonly List<ChildAnimatorState> states = new List<ChildAnimatorState>();
        private readonly HashSet<string> stateNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<AnimatorState, List<AnimatorStateTransition>> transitions =
            new Dictionary<AnimatorState, List<AnimatorStateTransition>>();
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        internal GeneratedAnimatorLayer(AnimatorController controller, string name)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.name = controller.MakeUniqueLayerName(name);
            Machine = new AnimatorStateMachine { name = this.name };
            Keep(Machine);
        }

        internal AnimatorStateMachine Machine { get; }
        internal AnimatorState DefaultState { get; set; }
        internal int StateCount => states.Count;

        internal AnimatorState State(string stateName, Motion motion = null)
        {
            var unique = stateName;
            for (var suffix = 1; !stateNames.Add(unique); suffix++)
                unique = stateName + " " + suffix;
            var state = new AnimatorState { name = unique, motion = motion };
            var index = states.Count;
            states.Add(new ChildAnimatorState
            {
                state = state,
                position = new Vector3(300f + index % 8 * 260f, index / 8 * 60f, 0f)
            });
            transitions[state] = new List<AnimatorStateTransition>();
            return Keep(state);
        }

        /// <summary>Unity's defaults for AddTransition: no exit time, fixed duration.</summary>
        internal AnimatorStateTransition Transition(AnimatorState from, AnimatorState to)
        {
            var transition = new AnimatorStateTransition
            {
                destinationState = to,
                hasExitTime = false,
                hasFixedDuration = true
            };
            transitions[from].Add(transition);
            return Keep(transition);
        }

        /// <summary>Clips, drivers and other objects the layer references.</summary>
        internal T Keep<T>(T value) where T : UnityEngine.Object
        {
            value.hideFlags = HideFlags.HideInHierarchy;
            created.Add(value);
            return value;
        }

        /// <summary>Assigns every array once, attaches all objects in one batch and
        /// appends the layer.</summary>
        internal void Commit(float defaultWeight)
        {
            foreach (var pair in transitions)
                pair.Key.transitions = pair.Value.ToArray();
            Machine.states = states.ToArray();
            Machine.defaultState = DefaultState != null
                ? DefaultState
                : states.Count > 0 ? states[0].state : null;

            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(controller)))
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var value in created)
                        AssetDatabase.AddObjectToAsset(value, controller);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

            controller.layers = controller.layers.Concat(new[]
            {
                new AnimatorControllerLayer
                {
                    name = name,
                    stateMachine = Machine,
                    defaultWeight = defaultWeight,
                    blendingMode = AnimatorLayerBlendingMode.Override
                }
            }).ToArray();
        }
    }

    internal static class GeneratedAnimatorExtensions
    {
        /// <summary>AddCondition without its per-call Undo step.</summary>
        internal static void Require(
            this AnimatorTransitionBase transition,
            AnimatorConditionMode mode,
            float threshold,
            string parameter)
        {
            transition.conditions = transition.conditions.Concat(new[]
            {
                new AnimatorCondition { mode = mode, threshold = threshold, parameter = parameter }
            }).ToArray();
        }

        /// <summary>AddParameter without its per-call Undo step.</summary>
        internal static void AppendParameter(
            this AnimatorController controller,
            AnimatorControllerParameter parameter)
        {
            controller.parameters = controller.parameters.Concat(new[] { parameter }).ToArray();
        }
    }
}
