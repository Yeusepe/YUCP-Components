using System;
using System.Collections.Generic;
using System.Linq;

namespace YUCP.Components.Editor
{


    public static class FTBlendSyncCatalog
    {
        public sealed class Shape
        {
            public string ue, arkit, sranipal;
            public string[] aliases;
            public Shape(string ue, string arkit = null, string sranipal = null, params string[] aliases)
            { this.ue = ue; this.arkit = arkit; this.sranipal = sranipal; this.aliases = aliases; }
            public IEnumerable<string> Names => new[] { ue, arkit, sranipal }.Concat(aliases).Where(n => !string.IsNullOrEmpty(n));
        }

        public static readonly List<Shape> Shapes = CreateShapes();
        public static readonly Dictionary<string, string[]> Combinations = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "CheekPuff", new[] { "CheekPuffLeft", "CheekPuffRight" } },
            { "CheekSuck", new[] { "CheekSuckLeft", "CheekSuckRight" } },
            { "BrowInnerUp", new[] { "BrowInnerUpLeft", "BrowInnerUpRight" } },
            { "EyeClosed", new[] { "EyeClosedLeft", "EyeClosedRight" } },
            { "EyeDilation", new[] { "EyeDilationLeft", "EyeDilationRight" } },
            { "EyeConstrict", new[] { "EyeConstrictLeft", "EyeConstrictRight" } },
            { "LipFunnel", new[] { "LipFunnelUpper", "LipFunnelLower" } },
            { "LipPucker", new[] { "LipPuckerUpper", "LipPuckerLower" } },
            { "MouthLowerDown", new[] { "MouthLowerDownLeft", "MouthLowerDownRight" } },
            { "MouthUpperUp", new[] { "MouthUpperUpLeft", "MouthUpperUpRight" } },
            { "MouthPress", new[] { "MouthPressLeft", "MouthPressRight" } },
            { "MouthSmile", new[] { "MouthSmileLeft", "MouthSmileRight" } },
            { "MouthFrown", new[] { "MouthFrownLeft", "MouthFrownRight" } },
            { "MouthLeft", new[] { "MouthUpperLeft", "MouthLowerLeft" } },
            { "MouthRight", new[] { "MouthUpperRight", "MouthLowerRight" } },
            { "NoseSneer", new[] { "NoseSneerLeft", "NoseSneerRight" } }
        };

        public static readonly Dictionary<string, string> Mmd = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "あ", "VowelA" }, { "い", "VowelI" }, { "う", "VowelU" }, { "え", "VowelE" }, { "お", "VowelO" },
            { "まばたき", "EyeClosed" }, { "ウィンク", "EyeClosedLeft" }, { "ウィンク右", "EyeClosedRight" },
            { "ウィンク２", "EyeClosedLeft" }, { "ウィンク２右", "EyeClosedRight" }, { "ｳｨﾝｸ２右", "EyeClosedRight" },
            { "笑い", "HappyEyes" }, { "びっくり", "WideEyes" }, { "にこり", "HappyBrow" },
            { "にっこり", "MouthSmile" }, { "口角上げ", "MouthSmile" }, { "口角下げ", "MouthFrown" }, { "瞳小", "EyeConstrict" },
            { "にやり", "MouthSmile" }, { "への字", "MouthFrown" }, { "怒り", "AngryBrow" },
            { "困る", "BrowInnerUp" }, { "ぺろっ", "TongueOut" }
        };

        public static IEnumerable<KeyValuePair<string, string>> Targets(FTBlendSyncStandard standards)
        {
            foreach (var shape in Shapes)
            {
                if ((standards & FTBlendSyncStandard.UnifiedExpressions) != 0 && !shape.ue.StartsWith("Vowel"))
                    yield return new KeyValuePair<string, string>(shape.ue, shape.ue);
                if ((standards & FTBlendSyncStandard.ARKit) != 0 && shape.arkit != null)
                    yield return new KeyValuePair<string, string>(shape.arkit, shape.ue);
                if ((standards & FTBlendSyncStandard.SRanipal) != 0 && shape.sranipal != null)
                    yield return new KeyValuePair<string, string>(shape.sranipal, shape.ue);
            }
            if ((standards & FTBlendSyncStandard.MMD) != 0)
                foreach (var item in Mmd) yield return item;
            if ((standards & FTBlendSyncStandard.SRanipal) != 0)
            {
                foreach (var name in new[] { "Mouth_Ape_Shape", "Mouth_Upper_Overturn", "Mouth_Lower_Overturn", "Eye_Left_squeeze", "Eye_Right_squeeze", "Tongue_LongStep1", "Tongue_LongStep2" })
                    yield return new KeyValuePair<string, string>(name, name);

                foreach (var side in new[] { "Left", "Right" })
                {
                    yield return new KeyValuePair<string, string>("Eye_" + side + "_Up", "EyeLookUp" + side);
                    yield return new KeyValuePair<string, string>("Eye_" + side + "_Down", "EyeLookDown" + side);
                    yield return new KeyValuePair<string, string>("Mouth_Upper_Up" + side, "MouthUpperUp" + side);
                    yield return new KeyValuePair<string, string>("Mouth_Lower_Down" + side, "MouthLowerDown" + side);
                }
            }
        }

        private static List<Shape> CreateShapes()
        {
            var list = new List<Shape>
            {
                new Shape("BrowInnerUp", "browInnerUp"),
                new Shape("CheekPuff", "cheekPuff"), new Shape("CheekSuck", null, "Cheek_Suck"),
                new Shape("JawOpen", "jawOpen", "Jaw_Open"), new Shape("JawForward", "jawForward", "Jaw_Forward"),
                new Shape("JawLeft", "jawLeft", "Jaw_Left"), new Shape("JawRight", "jawRight", "Jaw_Right"),
                new Shape("MouthClosed", "mouthClose"), new Shape("LipFunnel", "mouthFunnel"),
                new Shape("LipFunnelUpper"), new Shape("LipFunnelLower"), new Shape("LipPucker", "mouthPucker", "Mouth_Pout"),
                new Shape("LipPuckerUpper"), new Shape("LipPuckerLower"),
                new Shape("LipSuckUpper", "mouthRollUpper", "Mouth_Upper_Inside"),
                new Shape("LipSuckLower", "mouthRollLower", "Mouth_Lower_Inside"),
                new Shape("MouthRaiserUpper", "mouthShrugUpper"), new Shape("MouthRaiserLower", "mouthShrugLower", "Mouth_Lower_Overlay"),
                new Shape("MouthLeft", "mouthLeft", "Mouth_Left"), new Shape("MouthRight", "mouthRight", "Mouth_Right"),
                new Shape("MouthLowerDown"), new Shape("MouthUpperUp"), new Shape("MouthPress"), new Shape("NoseSneer"),
                new Shape("EyeClosed", null, null, "まばたき", "Vrc.Blink", "blink"),
                new Shape("EyeDilation", null, null, "Pupil Grow"), new Shape("EyeConstrict", null, null, "Pupil Shrink"),
                new Shape("TongueOut", "tongueOut", null, "ぺろっ"),
                new Shape("TongueOutStep1", null, null, "Tongue_LongStep1"), new Shape("TongueOutStep2", null, null, "Tongue_LongStep2"),
                new Shape("TongueLeft", null, "Tongue_Left"), new Shape("TongueRight", null, "Tongue_Right"),
                new Shape("TongueUp", null, "Tongue_Up"), new Shape("TongueDown", null, "Tongue_Down"), new Shape("TongueRoll", null, "Tongue_Roll"),
                new Shape("VowelA", null, null, "あ", "vrc.v_aa"), new Shape("VowelI", null, null, "い", "vrc.v_ih"),
                new Shape("VowelU", null, null, "う", "vrc.v_ou"), new Shape("VowelE", null, null, "え", "vrc.v_e"),
                new Shape("VowelO", null, null, "お", "vrc.v_oh")
            };
            foreach (var side in new[] { "Left", "Right" })
            {
                foreach (var name in new[] { "BrowDown", "BrowOuterUp", "CheekSquint", "EyeSquint", "NoseSneer", "MouthDimple", "MouthPress", "MouthStretch", "MouthFrown" })
                    list.Add(new Shape(name + side, char.ToLowerInvariant(name[0]) + name.Substring(1) + side));
                list.Add(new Shape("BrowInnerUp" + side));
                list.Add(new Shape("EyeClosed" + side, "eyeBlink" + side, "Eye_" + side + "_Blink", side == "Left" ? "ウィンク" : "ウィンク右"));
                list.Add(new Shape("EyeWide" + side, "eyeWide" + side, "Eye_" + side + "_Wide"));
                list.Add(new Shape("EyeDilation" + side, null, "Eye_" + side + "_Dilation"));
                list.Add(new Shape("EyeConstrict" + side, null, "Eye_" + side + "_Constrict"));
                foreach (var direction in new[] { "Up", "Down", "In", "Out" })
                {
                    string sr = direction == "In" ? (side == "Left" ? "Right" : "Left") : direction == "Out" ? side : "Look_" + direction;
                    list.Add(new Shape("EyeLook" + direction + side, "eyeLook" + direction + side, "Eye_" + side + "_" + sr, "Eye_" + side + "_" + direction));
                }
                foreach (var name in new[] { "CheekPuff", "CheekSuck" })
                    list.Add(new Shape(name + side, null, name == "CheekPuff" ? "Cheek_Puff_" + side : null));
                list.Add(new Shape("MouthSmile" + side, "mouthSmile" + side, "Mouth_Smile_" + side));
                list.Add(new Shape("MouthSad" + side, null, "Mouth_Sad_" + side));
                list.Add(new Shape("MouthUpperUp" + side, "mouthUpperUp" + side, "Mouth_Upper_Up_" + side, "Mouth_Upper_Up" + side));
                list.Add(new Shape("MouthLowerDown" + side, "mouthLowerDown" + side, "Mouth_Lower_Down_" + side, "Mouth_Lower_Down" + side));
                list.Add(new Shape("MouthUpper" + side, null, "Mouth_Upper_" + side));
                list.Add(new Shape("MouthLower" + side, null, "Mouth_Lower_" + side));
                list.Add(new Shape("MouthTightener" + side));
            }
            return list;
        }
    }
}
