using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;

namespace YUCP.Components.Editor.Tests
{
    public sealed class ParameterCompressionCatalogTests
    {
        [Test]
        public void PersistentMenuStateIsEligibleWhileEdgesAndLiveInputsAreProtected()
        {
            using (var fixture = new CatalogFixture(
                       Bool("Menu/Toggle"),
                       Bool("Menu/RadialGate"),
                       Float("Menu/Radial"),
                       Bool("Menu/Button"),
                       Bool("Menu/PuppetGate"),
                       Float("Menu/PuppetX"),
                       Float("Menu/PuppetY"),
                       Bool("Contact/Active"),
                       Float("Voice")))
            {
                fixture.menu.controls.Add(Toggle("Menu/Toggle"));
                fixture.menu.controls.Add(Radial(
                    "Menu/RadialGate", "Menu/Radial"));
                fixture.menu.controls.Add(Button("Menu/Button"));
                fixture.menu.controls.Add(TwoAxis(
                    "Menu/PuppetGate", "Menu/PuppetX", "Menu/PuppetY"));

                var receiver = fixture.root.AddComponent<VRCContactReceiver>();
                receiver.parameter = "Contact/Active";

                var catalog = fixture.Scan();

                AssertEligible(catalog, "Menu/Toggle",
                    ParameterCompressionMenuRole.Toggle);
                AssertEligible(catalog, "Menu/Radial",
                    ParameterCompressionMenuRole.Radial);

                AssertProtected(catalog, "Menu/RadialGate",
                    ParameterCompressionMenuRole.SubMenuGate, hardUnsafe: true);
                AssertProtected(catalog, "Menu/Button",
                    ParameterCompressionMenuRole.Button, hardUnsafe: true);
                AssertProtected(catalog, "Menu/PuppetGate",
                    ParameterCompressionMenuRole.SubMenuGate, hardUnsafe: true);
                AssertProtected(catalog, "Menu/PuppetX",
                    ParameterCompressionMenuRole.Puppet, hardUnsafe: false);
                AssertProtected(catalog, "Menu/PuppetY",
                    ParameterCompressionMenuRole.Puppet, hardUnsafe: false);
                AssertProtected(catalog, "Contact/Active",
                    ParameterCompressionMenuRole.None, hardUnsafe: true);
                AssertProtected(catalog, "Voice",
                    ParameterCompressionMenuRole.None, hardUnsafe: true);
            }
        }

        [Test]
        public void UnknownParameterNeedsTheExplicitUnverifiedOverride()
        {
            using (var fixture = new CatalogFixture(
                       Float("Unknown/Checked"),
                       Float("Unknown/Override")))
            {
                fixture.compressor.automaticSelection = false;
                fixture.compressor.rules.Add(new ParameterCompressionRule
                {
                    parameterName = "Unknown/Checked",
                    selection = ParameterCompressionRuleSelection.Include
                });
                fixture.compressor.rules.Add(new ParameterCompressionRule
                {
                    parameterName = "Unknown/Override",
                    selection = ParameterCompressionRuleSelection.IncludeUnverified
                });

                var catalog = fixture.Scan();
                var checkedEntry = Entry(catalog, "Unknown/Checked");
                var overrideEntry = Entry(catalog, "Unknown/Override");

                Assert.That(checkedEntry.explicitlyIncluded, Is.True);
                Assert.That(checkedEntry.allowUnverified, Is.False);
                Assert.That(checkedEntry.eligible, Is.False);
                Assert.That(checkedEntry.hardUnsafe, Is.True);
                Assert.That(checkedEntry.reason, Does.Contain("Include Unverified"));

                Assert.That(overrideEntry.explicitlyIncluded, Is.True);
                Assert.That(overrideEntry.allowUnverified, Is.True);
                Assert.That(overrideEntry.eligible, Is.True);
                Assert.That(overrideEntry.hardUnsafe, Is.False);
                Assert.That(overrideEntry.reason,
                    Does.Contain("explicitly included").IgnoreCase);
            }
        }

        [Test]
        public void IncludedUnsyncedParameterIsRejectedAsAlreadyFree()
        {
            using (var fixture = new CatalogFixture(
                       Float("Local/Only", networkSynced: false)))
            {
                fixture.compressor.rules.Add(new ParameterCompressionRule
                {
                    parameterName = "Local/Only",
                    selection = ParameterCompressionRuleSelection.IncludeUnverified
                });
                var catalog = fixture.Scan();
                var validate = typeof(ParameterCompressorProcessor).GetMethod(
                    "ValidateRules",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(validate, Is.Not.Null,
                    "The processor rule validator was renamed without updating its tests.");

                var arguments = new object[]
                {
                    fixture.compressor,
                    fixture.parameters,
                    catalog,
                    null
                };
                var accepted = (bool)validate.Invoke(null, arguments);

                Assert.That(accepted, Is.False);
                Assert.That(arguments[3], Is.TypeOf<string>());
                Assert.That((string)arguments[3], Does.Contain("not synchronized"));
                Assert.That((string)arguments[3], Does.Contain("costs no VRChat"));
            }
        }

        [Test]
        public void CompactAdvancedVisemeTuningIsARequiredSharedProducer()
        {
            using (var fixture = new CatalogFixture())
            {
                fixture.compressor.automaticSelection = false;
                var reconstructor = fixture.root.AddComponent<
                    AdvancedVisemeReconstructorData>();
                reconstructor.createTuningMenu = true;
                reconstructor.tuningSyncMode =
                    AdvancedVisemeTuningSyncMode.CompactSynced;
                var control = AdvancedVisemeTuning.Controls.First();
                var name = reconstructor.TuningParameterName(control);
                fixture.SetParameters(Float(name));

                var compact = Entry(fixture.Scan(), name);
                Assert.That(compact.explicitlyIncluded, Is.True,
                    "Producer registration is how the processor marks a candidate required.");
                Assert.That(compact.eligible, Is.True);
                Assert.That(compact.reason, Does.Contain("Registered by a YUCP producer"));

                reconstructor.tuningSyncMode =
                    AdvancedVisemeTuningSyncMode.LocalOnly;
                var local = Entry(fixture.Scan(), name);
                Assert.That(local.explicitlyIncluded, Is.True,
                    "Legacy LocalOnly assets are upgraded to shared tuning during validation.");
                Assert.That(local.eligible, Is.True);
                Assert.That(local.reason, Does.Contain("Registered by a YUCP producer"));
            }
        }

        [Test]
        public void ProcessorRunsImmediatelyBeforeVrcFuryParameterCompression()
        {
            Assert.That(new ParameterCompressorProcessor().callbackOrder,
                Is.EqualTo(int.MaxValue - 101));
            Assert.That(new ParameterCompressorFinalValidator().callbackOrder,
                Is.EqualTo(int.MaxValue - 1));
        }

        private static void AssertEligible(
            ParameterCompressionCatalog catalog,
            string name,
            ParameterCompressionMenuRole expectedRole)
        {
            var entry = Entry(catalog, name);
            Assert.That(entry.menuRole, Is.EqualTo(expectedRole), name);
            Assert.That(entry.eligible, Is.True, name + ": " + entry.reason);
            Assert.That(entry.hardUnsafe, Is.False, name);
        }

        private static void AssertProtected(
            ParameterCompressionCatalog catalog,
            string name,
            ParameterCompressionMenuRole expectedRole,
            bool hardUnsafe)
        {
            var entry = Entry(catalog, name);
            Assert.That(entry.menuRole, Is.EqualTo(expectedRole), name);
            Assert.That(entry.eligible, Is.False, name);
            Assert.That(entry.hardUnsafe, Is.EqualTo(hardUnsafe),
                name + ": " + entry.reason);
            Assert.That(entry.reason, Is.Not.Empty, name);
        }

        private static ParameterCompressionCatalogEntry Entry(
            ParameterCompressionCatalog catalog,
            string name)
        {
            return catalog.entries.Single(entry =>
                string.Equals(entry.parameter.name, name, StringComparison.Ordinal));
        }

        private static VRCExpressionParameters.Parameter Bool(
            string name,
            bool networkSynced = true)
        {
            return Parameter(name, VRCExpressionParameters.ValueType.Bool,
                networkSynced);
        }

        private static VRCExpressionParameters.Parameter Float(
            string name,
            bool networkSynced = true)
        {
            return Parameter(name, VRCExpressionParameters.ValueType.Float,
                networkSynced);
        }

        private static VRCExpressionParameters.Parameter Parameter(
            string name,
            VRCExpressionParameters.ValueType type,
            bool networkSynced)
        {
            return new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                saved = true,
                networkSynced = networkSynced
            };
        }

        private static VRCExpressionsMenu.Control Toggle(string parameter)
        {
            return Control(VRCExpressionsMenu.Control.ControlType.Toggle,
                parameter);
        }

        private static VRCExpressionsMenu.Control Button(string parameter)
        {
            return Control(VRCExpressionsMenu.Control.ControlType.Button,
                parameter);
        }

        private static VRCExpressionsMenu.Control Radial(
            string gate,
            string value)
        {
            var control = Control(
                VRCExpressionsMenu.Control.ControlType.RadialPuppet, gate);
            control.subParameters = new[] { ParameterReference(value) };
            return control;
        }

        private static VRCExpressionsMenu.Control TwoAxis(
            string gate,
            string x,
            string y)
        {
            var control = Control(
                VRCExpressionsMenu.Control.ControlType.TwoAxisPuppet, gate);
            control.subParameters = new[]
            {
                ParameterReference(x),
                ParameterReference(y)
            };
            return control;
        }

        private static VRCExpressionsMenu.Control Control(
            VRCExpressionsMenu.Control.ControlType type,
            string parameter)
        {
            return new VRCExpressionsMenu.Control
            {
                name = parameter,
                type = type,
                parameter = ParameterReference(parameter),
                subParameters = Array.Empty<VRCExpressionsMenu.Control.Parameter>()
            };
        }

        private static VRCExpressionsMenu.Control.Parameter ParameterReference(
            string name)
        {
            return new VRCExpressionsMenu.Control.Parameter { name = name };
        }

        private sealed class CatalogFixture : IDisposable
        {
            internal readonly GameObject root;
            internal readonly VRCAvatarDescriptor descriptor;
            internal readonly ParameterCompressorData compressor;
            internal readonly VRCExpressionParameters parameters;
            internal readonly VRCExpressionsMenu menu;

            internal CatalogFixture(
                params VRCExpressionParameters.Parameter[] declarations)
            {
                root = new GameObject("Parameter Compressor Catalog Test");
                descriptor = root.AddComponent<VRCAvatarDescriptor>();
                descriptor.baseAnimationLayers =
                    Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                descriptor.specialAnimationLayers =
                    Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
                compressor = root.AddComponent<ParameterCompressorData>();
                parameters = ScriptableObject.CreateInstance<
                    VRCExpressionParameters>();
                menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                menu.controls = new List<VRCExpressionsMenu.Control>();
                descriptor.expressionParameters = parameters;
                descriptor.expressionsMenu = menu;
                SetParameters(declarations);
            }

            internal void SetParameters(
                params VRCExpressionParameters.Parameter[] declarations)
            {
                parameters.parameters = declarations ??
                                        Array.Empty<VRCExpressionParameters.Parameter>();
            }

            internal ParameterCompressionCatalog Scan()
            {
                return ParameterCompressionCatalog.Scan(
                    root, descriptor, compressor);
            }

            public void Dispose()
            {
                descriptor.expressionParameters = null;
                descriptor.expressionsMenu = null;
                UnityEngine.Object.DestroyImmediate(menu);
                UnityEngine.Object.DestroyImmediate(parameters);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
