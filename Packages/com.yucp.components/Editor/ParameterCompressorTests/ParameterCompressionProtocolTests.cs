using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace YUCP.Components.Editor.Tests
{
    public sealed class ParameterCompressionProtocolTests
    {
        [TestCase(3, 1, 3, 2)]
        [TestCase(4, 2, 6, 5)]
        [TestCase(5, 2, 10, 9)]
        [TestCase(6, 3, 20, 19)]
        [TestCase(7, 3, 35, 34)]
        public void ConstantWeightAlphabetHasExactBinomialCardinality(
            int wireBits,
            int weight,
            int alphabetSize,
            int radix)
        {
            var alphabet = new ParameterCompressionAlphabet(wireBits);

            Assert.That(alphabet.Weight, Is.EqualTo(weight));
            Assert.That(alphabet.Codewords, Has.Count.EqualTo(alphabetSize));
            Assert.That(alphabet.Radix, Is.EqualTo(radix));
            Assert.That(ParameterCompressionAlphabet.Binomial(wireBits, weight),
                Is.EqualTo(alphabetSize));
            Assert.That(alphabet.Codewords.Distinct().Count(), Is.EqualTo(alphabetSize));
            Assert.That(alphabet.Codewords, Has.All.Matches<int>(word =>
                ParameterCompressionAlphabet.PopCount(word) == weight));
            Assert.That(alphabet.IsCodeword(ParameterCompressionProtocol.SpacerWord),
                Is.False);
            Assert.That(alphabet.SyncWord, Is.EqualTo(alphabet.Codewords.Last()));

            for (var digit = 0; digit < alphabet.Radix; digit++)
            {
                var word = alphabet.EncodeDigit(digit);
                Assert.That(word, Is.Not.EqualTo(alphabet.SyncWord));
                Assert.That(alphabet.TryDecodeDigit(word, out var decoded), Is.True);
                Assert.That(decoded, Is.EqualTo(digit));
            }
            Assert.That(alphabet.TryDecodeDigit(alphabet.SyncWord, out _), Is.False);
        }

        [Test]
        public void SixWireTwentySixFloatDomainsNeedExactlyThreeDigits()
        {
            var domains = Enumerable.Range(0, 26)
                .Select(index => new ParameterCompressionDomain(
                    "Float/" + index.ToString("D2"),
                    ParameterCompressionProtocol.VrChatFloatCardinality))
                .ToArray();
            var layout = new ParameterCompressionEnumerativeLayout(domains);
            var protocol = new ParameterCompressionProtocol(layout, 6);

            Assert.That(protocol.Alphabet.Radix, Is.EqualTo(19));
            Assert.That(layout.TotalCardinality, Is.EqualTo(26L * 255L));
            Assert.That(ParameterCompressionEnumerativeLayout.Capacity(19, 2),
                Is.EqualTo(361));
            Assert.That(ParameterCompressionEnumerativeLayout.Capacity(19, 3),
                Is.EqualTo(6859));
            Assert.That(protocol.DigitCount, Is.EqualTo(3));
            Assert.That(protocol.FrameWordCount, Is.EqualTo(11));

            for (var channel = 0; channel < domains.Length; channel++)
            {
                var entry = layout.Entries[channel];
                Assert.That(entry.Offset, Is.EqualTo(channel * 255L));
                foreach (var value in new[] { 0, 127, 254 })
                {
                    var code = layout.Encode(channel, value);
                    Assert.That(layout.TryDecode(code, out var decodedEntry,
                        out var decodedValue), Is.True);
                    Assert.That(decodedEntry.ChannelIndex, Is.EqualTo(channel));
                    Assert.That(decodedEntry.Name, Is.EqualTo(domains[channel].Name));
                    Assert.That(decodedValue, Is.EqualTo(value));
                }
            }
        }

        [Test]
        public void MixedEnumerativeOffsetsRoundTripEveryValue()
        {
            var domains = new[]
            {
                new ParameterCompressionDomain("Bool", 2),
                new ParameterCompressionDomain("SmallInt", 7),
                new ParameterCompressionDomain("Float", 255),
                new ParameterCompressionDomain("Int", 256)
            };
            var layout = new ParameterCompressionEnumerativeLayout(domains);

            Assert.That(layout.Entries.Select(entry => entry.Offset),
                Is.EqualTo(new long[] { 0, 2, 9, 264 }));
            Assert.That(layout.TotalCardinality, Is.EqualTo(520));
            for (var channel = 0; channel < domains.Length; channel++)
            for (var value = 0; value < domains[channel].Cardinality; value++)
            {
                var encoded = layout.Encode(channel, value);
                Assert.That(layout.TryDecode(encoded, out var entry, out var decoded),
                    Is.True);
                Assert.That(entry.ChannelIndex, Is.EqualTo(channel));
                Assert.That(decoded, Is.EqualTo(value));
            }
            Assert.That(layout.TryDecode(-1, out _, out _), Is.False);
            Assert.That(layout.TryDecode(layout.TotalCardinality, out _, out _),
                Is.False);
        }

        [Test]
        public void ExactDigitMathHasNoFloatingPointBoundaryErrors()
        {
            foreach (var radix in new[] { 2, 5, 9, 19, 34 })
            for (var digits = 0; digits <= 8; digits++)
            {
                var capacity = ParameterCompressionEnumerativeLayout.Capacity(
                    radix, digits);
                Assert.That(ParameterCompressionEnumerativeLayout.DigitsRequired(
                        capacity, radix),
                    Is.EqualTo(digits));
                if (capacity < long.MaxValue)
                    Assert.That(ParameterCompressionEnumerativeLayout.DigitsRequired(
                            capacity + 1, radix),
                        Is.EqualTo(digits + 1));
            }
        }

        [Test]
        public void VrChatFloatQuantizerRoundTripsAllTwoHundredFiftyFiveValues()
        {
            for (var code = 0;
                 code < ParameterCompressionProtocol.VrChatFloatCardinality;
                 code++)
            {
                var value = ParameterCompressionProtocol.DequantizeVrChatFloat(code);
                Assert.That(ParameterCompressionProtocol.QuantizeVrChatFloat(value),
                    Is.EqualTo(code), "VRChat Float code " + code);
            }

            Assert.That(ParameterCompressionProtocol.DequantizeVrChatFloat(0),
                Is.EqualTo(-1f));
            Assert.That(ParameterCompressionProtocol.DequantizeVrChatFloat(127),
                Is.Zero);
            Assert.That(ParameterCompressionProtocol.DequantizeVrChatFloat(254),
                Is.EqualTo(1f));
            Assert.That(ParameterCompressionProtocol.QuantizeVrChatFloat(-2f),
                Is.Zero);
            Assert.That(ParameterCompressionProtocol.QuantizeVrChatFloat(2f),
                Is.EqualTo(254));
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void ZeroSpacedTornTransitionsNeverFormAnotherCodeword(int wireBits)
        {
            var alphabet = new ParameterCompressionAlphabet(wireBits);
            foreach (var word in alphabet.Codewords)
            {
                var setBits = Enumerable.Range(0, wireBits)
                    .Where(bit => (word & 1 << bit) != 0)
                    .ToArray();
                foreach (var order in Permutations(setBits))
                {
                    var clearing = word;
                    for (var index = 0; index < order.Length; index++)
                    {
                        clearing &= ~(1 << order[index]);
                        if (index < order.Length - 1)
                            Assert.That(alphabet.IsCodeword(clearing), Is.False,
                                $"{wireBits}-wire clear tore into {clearing}.");
                    }
                    Assert.That(clearing, Is.EqualTo(0));

                    var setting = 0;
                    for (var index = 0; index < order.Length; index++)
                    {
                        setting |= 1 << order[index];
                        if (index < order.Length - 1)
                            Assert.That(alphabet.IsCodeword(setting), Is.False,
                                $"{wireBits}-wire set tore into {setting}.");
                    }
                    Assert.That(setting, Is.EqualTo(word));
                }
            }
        }

        [Test]
        public void StreamingDecoderIgnoresTornIntermediatesAndCommitsOnce()
        {
            var layout = new ParameterCompressionEnumerativeLayout(new[]
            {
                new ParameterCompressionDomain("Target", 255)
            });
            var protocol = new ParameterCompressionProtocol(layout, 6);
            var decoder = protocol.CreateDecoder();
            var frame = protocol.EncodeFrame(0, 193);
            var expanded = ExpandWithTornTransitions(frame, 6).ToArray();
            var commits = new List<ParameterCompressionDecodedValue>();

            foreach (var word in expanded)
                if (decoder.TryPush(word, out var decoded)) commits.Add(decoded);

            Assert.That(commits, Has.Count.EqualTo(1));
            Assert.That(commits[0].Name, Is.EqualTo("Target"));
            Assert.That(commits[0].ValueCode, Is.EqualTo(193));
        }

        [Test]
        public void CorruptFramesDoNotCommitAndNextSyncResynchronizes()
        {
            var layout = new ParameterCompressionEnumerativeLayout(new[]
            {
                new ParameterCompressionDomain("A", 255),
                new ParameterCompressionDomain("B", 255)
            });
            var protocol = new ParameterCompressionProtocol(layout, 6);
            var decoder = protocol.CreateDecoder();
            var commits = new List<ParameterCompressionDecodedValue>();

            Push(protocol.EncodeFrame("A", 41));

            var missingDigit = protocol.EncodeFrame("B", 92).ToList();
            // A payload digit and its following spacer are one structural word.
            missingDigit.RemoveRange(3, 2);
            Push(missingDigit);

            var extraDigit = protocol.EncodeFrame("B", 93).ToList();
            extraDigit.Insert(3, ParameterCompressionProtocol.SpacerWord);
            extraDigit.Insert(4, protocol.Alphabet.EncodeDigit(0));
            extraDigit.Insert(5, ParameterCompressionProtocol.SpacerWord);
            Push(extraDigit);

            Push(protocol.EncodeFrame("B", 94));

            Assert.That(commits.Select(commit => commit.Name + ":" + commit.ValueCode),
                Is.EqualTo(new[] { "A:41", "B:94" }));

            void Push(IEnumerable<int> words)
            {
                foreach (var word in words)
                    if (decoder.TryPush(word, out var decoded)) commits.Add(decoded);
            }
        }

        [Test]
        public void DifferentCodewordWithoutZeroSpacerInvalidatesFrame()
        {
            var layout = new ParameterCompressionEnumerativeLayout(new[]
            {
                new ParameterCompressionDomain("Target", 255)
            });
            var protocol = new ParameterCompressionProtocol(layout, 6);
            var decoder = protocol.CreateDecoder();
            var corrupt = protocol.EncodeFrame(0, 17).ToList();
            corrupt.Insert(4, protocol.Alphabet.EncodeDigit(1));
            var commits = new List<int>();
            foreach (var word in corrupt)
                if (decoder.TryPush(word, out var decoded)) commits.Add(decoded.ValueCode);
            Assert.That(commits, Is.Empty);

            foreach (var word in protocol.EncodeFrame(0, 18))
                if (decoder.TryPush(word, out var decoded)) commits.Add(decoded.ValueCode);
            Assert.That(commits, Is.EqualTo(new[] { 18 }));
        }

        private static IEnumerable<int[]> Permutations(int[] values)
        {
            var copy = (int[])values.Clone();
            return Permute(copy, 0);
        }

        private static IEnumerable<int[]> Permute(int[] values, int index)
        {
            if (index >= values.Length)
            {
                yield return (int[])values.Clone();
                yield break;
            }
            for (var cursor = index; cursor < values.Length; cursor++)
            {
                (values[index], values[cursor]) = (values[cursor], values[index]);
                foreach (var permutation in Permute(values, index + 1))
                    yield return permutation;
                (values[index], values[cursor]) = (values[cursor], values[index]);
            }
        }

        private static IEnumerable<int> ExpandWithTornTransitions(
            IReadOnlyList<int> settled,
            int wireBits)
        {
            if (settled.Count == 0) yield break;
            yield return settled[0];
            for (var index = 1; index < settled.Count; index++)
            {
                var previous = settled[index - 1];
                var next = settled[index];
                var changingBits = Enumerable.Range(0, wireBits)
                    .Where(bit => ((previous ^ next) & 1 << bit) != 0)
                    .OrderByDescending(bit => bit)
                    .ToArray();
                var current = previous;
                foreach (var bit in changingBits)
                {
                    current ^= 1 << bit;
                    yield return current;
                }
            }
        }
    }
}
