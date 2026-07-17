using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace YUCP.Components.Editor
{
    /// <summary>
    /// A constant-Hamming-weight wire alphabet. The all-zero word is deliberately
    /// absent and is used as a spacer. Moving from a codeword to zero (or back)
    /// changes bits in only one direction, so every torn intermediate has the
    /// wrong weight and cannot be mistaken for another symbol.
    /// </summary>
    internal sealed class ParameterCompressionAlphabet
    {
        private readonly int[] codewords;
        private readonly int[] payloadCodewords;
        private readonly Dictionary<int, int> digitByCodeword;

        internal ParameterCompressionAlphabet(int wireBits)
        {
            if (wireBits < 3 || wireBits > 16)
                throw new ArgumentOutOfRangeException(nameof(wireBits),
                    "A constant-weight parameter bus supports 3 through 16 wires.");

            WireBits = wireBits;
            Weight = wireBits / 2;
            var words = new List<int>();
            var limit = 1 << wireBits;
            for (var word = 1; word < limit; word++)
                if (PopCount(word) == Weight)
                    words.Add(word);

            var expected = Binomial(wireBits, Weight);
            if (words.Count != expected)
                throw new InvalidOperationException(
                    "The constant-weight alphabet was enumerated incorrectly.");
            if (words.Count < 3)
                throw new InvalidOperationException(
                    "The alphabet needs a synchronizer and at least two digits.");

            codewords = words.ToArray();
            SyncWord = codewords[codewords.Length - 1];
            payloadCodewords = codewords.Take(codewords.Length - 1).ToArray();
            digitByCodeword = payloadCodewords
                .Select((word, digit) => new { word, digit })
                .ToDictionary(pair => pair.word, pair => pair.digit);
            Codewords = new ReadOnlyCollection<int>(codewords);
        }

        internal int WireBits { get; }
        internal int Weight { get; }
        internal int Radix => payloadCodewords.Length;
        internal int SyncWord { get; }
        internal IReadOnlyList<int> Codewords { get; }

        internal int EncodeDigit(int digit)
        {
            if (digit < 0 || digit >= Radix)
                throw new ArgumentOutOfRangeException(nameof(digit));
            return payloadCodewords[digit];
        }

        internal bool TryDecodeDigit(int wireWord, out int digit)
        {
            return digitByCodeword.TryGetValue(wireWord, out digit);
        }

        internal bool IsCodeword(int wireWord)
        {
            if (wireWord <= 0 || wireWord >= (1 << WireBits)) return false;
            return PopCount(wireWord) == Weight;
        }

        internal static long Binomial(int n, int k)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
            if (k < 0 || k > n) return 0;
            k = Math.Min(k, n - k);
            long result = 1;
            for (var index = 1; index <= k; index++)
            {
                checked
                {
                    result = result * (n - k + index) / index;
                }
            }
            return result;
        }

        internal static int PopCount(int value)
        {
            var count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }
    }

    internal sealed class ParameterCompressionDomain
    {
        internal ParameterCompressionDomain(string name, int cardinality)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A domain needs a stable name.", nameof(name));
            if (cardinality <= 0)
                throw new ArgumentOutOfRangeException(nameof(cardinality));
            Name = name.Trim();
            Cardinality = cardinality;
        }

        internal string Name { get; }
        internal int Cardinality { get; }
    }

    internal sealed class ParameterCompressionLayoutEntry
    {
        internal ParameterCompressionLayoutEntry(
            string name,
            int channelIndex,
            long offset,
            int cardinality)
        {
            Name = name;
            ChannelIndex = channelIndex;
            Offset = offset;
            Cardinality = cardinality;
        }

        internal string Name { get; }
        internal int ChannelIndex { get; }
        internal long Offset { get; }
        internal int Cardinality { get; }
    }

    /// <summary>
    /// Assigns differently-sized parameter domains to contiguous ranges in one
    /// enumerative message space. This is a sum of cardinalities, not their
    /// Cartesian product: a packet carries one channel/value update.
    /// </summary>
    internal sealed class ParameterCompressionEnumerativeLayout
    {
        private readonly ParameterCompressionLayoutEntry[] entries;
        private readonly Dictionary<string, ParameterCompressionLayoutEntry> byName;

        internal ParameterCompressionEnumerativeLayout(
            IEnumerable<ParameterCompressionDomain> domains)
        {
            if (domains == null) throw new ArgumentNullException(nameof(domains));
            var materialized = domains.ToArray();
            if (materialized.Length == 0)
                throw new ArgumentException("A layout needs at least one domain.", nameof(domains));

            entries = new ParameterCompressionLayoutEntry[materialized.Length];
            byName = new Dictionary<string, ParameterCompressionLayoutEntry>(
                StringComparer.Ordinal);
            long offset = 0;
            for (var index = 0; index < materialized.Length; index++)
            {
                var domain = materialized[index] ??
                             throw new ArgumentException("A domain cannot be null.", nameof(domains));
                if (byName.ContainsKey(domain.Name))
                    throw new ArgumentException(
                        "Duplicate parameter compression domain '" + domain.Name + "'.",
                        nameof(domains));
                var entry = new ParameterCompressionLayoutEntry(
                    domain.Name, index, offset, domain.Cardinality);
                entries[index] = entry;
                byName.Add(entry.Name, entry);
                checked
                {
                    offset += domain.Cardinality;
                }
            }

            TotalCardinality = offset;
            Entries = new ReadOnlyCollection<ParameterCompressionLayoutEntry>(entries);
        }

        internal IReadOnlyList<ParameterCompressionLayoutEntry> Entries { get; }
        internal long TotalCardinality { get; }

        internal long Encode(int channelIndex, int valueCode)
        {
            if (channelIndex < 0 || channelIndex >= entries.Length)
                throw new ArgumentOutOfRangeException(nameof(channelIndex));
            var entry = entries[channelIndex];
            if (valueCode < 0 || valueCode >= entry.Cardinality)
                throw new ArgumentOutOfRangeException(nameof(valueCode));
            return entry.Offset + valueCode;
        }

        internal long Encode(string name, int valueCode)
        {
            if (name == null || !byName.TryGetValue(name, out var entry))
                throw new ArgumentOutOfRangeException(nameof(name));
            if (valueCode < 0 || valueCode >= entry.Cardinality)
                throw new ArgumentOutOfRangeException(nameof(valueCode));
            return entry.Offset + valueCode;
        }

        internal bool TryDecode(
            long enumerativeCode,
            out ParameterCompressionLayoutEntry entry,
            out int valueCode)
        {
            entry = null;
            valueCode = 0;
            if (enumerativeCode < 0 || enumerativeCode >= TotalCardinality)
                return false;

            var low = 0;
            var high = entries.Length - 1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                var candidate = entries[middle];
                if (enumerativeCode < candidate.Offset)
                {
                    high = middle - 1;
                    continue;
                }
                if (enumerativeCode >= candidate.Offset + candidate.Cardinality)
                {
                    low = middle + 1;
                    continue;
                }

                entry = candidate;
                valueCode = checked((int)(enumerativeCode - candidate.Offset));
                return true;
            }
            return false;
        }

        internal static int DigitsRequired(long cardinality, int radix)
        {
            if (cardinality <= 0)
                throw new ArgumentOutOfRangeException(nameof(cardinality));
            if (radix < 2) throw new ArgumentOutOfRangeException(nameof(radix));
            var digits = 0;
            long capacity = 1;
            while (capacity < cardinality)
            {
                checked
                {
                    capacity *= radix;
                }
                digits++;
            }
            return digits;
        }

        internal static long Capacity(int radix, int digits)
        {
            if (radix < 2) throw new ArgumentOutOfRangeException(nameof(radix));
            if (digits < 0) throw new ArgumentOutOfRangeException(nameof(digits));
            long capacity = 1;
            for (var index = 0; index < digits; index++)
            {
                checked
                {
                    capacity *= radix;
                }
            }
            return capacity;
        }

        internal static int[] EncodeDigits(long value, int radix, int digitCount)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            if (radix < 2) throw new ArgumentOutOfRangeException(nameof(radix));
            if (digitCount < 0) throw new ArgumentOutOfRangeException(nameof(digitCount));
            var digits = new int[digitCount];
            for (var index = digitCount - 1; index >= 0; index--)
            {
                digits[index] = (int)(value % radix);
                value /= radix;
            }
            if (value != 0)
                throw new ArgumentOutOfRangeException(nameof(value),
                    "The value does not fit in the requested digit count.");
            return digits;
        }

        internal static long DecodeDigits(IEnumerable<int> digits, int radix)
        {
            if (digits == null) throw new ArgumentNullException(nameof(digits));
            if (radix < 2) throw new ArgumentOutOfRangeException(nameof(radix));
            long value = 0;
            foreach (var digit in digits)
            {
                if (digit < 0 || digit >= radix)
                    throw new ArgumentOutOfRangeException(nameof(digits));
                checked
                {
                    value = value * radix + digit;
                }
            }
            return value;
        }
    }

    internal readonly struct ParameterCompressionDecodedValue
    {
        internal ParameterCompressionDecodedValue(
            string name,
            int channelIndex,
            int valueCode)
        {
            Name = name;
            ChannelIndex = channelIndex;
            ValueCode = valueCode;
        }

        internal string Name { get; }
        internal int ChannelIndex { get; }
        internal int ValueCode { get; }
    }

    /// <summary>
    /// Pure wire codec. Frames are spacer/sync/spacer/fixed payload/spacer/sync.
    /// A closing sync both commits the exact-length previous frame and begins a
    /// new one, which makes recovery independent of where observation starts.
    /// </summary>
    internal sealed class ParameterCompressionProtocol
    {
        internal const int SpacerWord = 0;
        internal const int VrChatFloatCardinality = 255;
        internal const int VrChatFloatZeroCode = 127;

        internal ParameterCompressionProtocol(
            ParameterCompressionEnumerativeLayout layout,
            int wireBits)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Alphabet = new ParameterCompressionAlphabet(wireBits);
            DigitCount = ParameterCompressionEnumerativeLayout.DigitsRequired(
                layout.TotalCardinality, Alphabet.Radix);
        }

        internal ParameterCompressionAlphabet Alphabet { get; }
        internal ParameterCompressionEnumerativeLayout Layout { get; }
        internal int DigitCount { get; }
        internal int FrameWordCount => 5 + 2 * DigitCount;

        internal int[] EncodeFrame(int channelIndex, int valueCode)
        {
            return EncodeEnumerativeCode(Layout.Encode(channelIndex, valueCode));
        }

        internal int[] EncodeFrame(string name, int valueCode)
        {
            return EncodeEnumerativeCode(Layout.Encode(name, valueCode));
        }

        internal ParameterCompressionStreamingDecoder CreateDecoder()
        {
            return new ParameterCompressionStreamingDecoder(this);
        }

        internal static int QuantizeVrChatFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value),
                    "A synchronized Float must be finite.");
            var clamped = Math.Max(-1d, Math.Min(1d, value));
            var scaled = clamped * 127d;
            var rounded = scaled >= 0d
                ? (int)Math.Floor(scaled + 0.5d)
                : (int)Math.Ceiling(scaled - 0.5d);
            return Math.Max(-127, Math.Min(127, rounded)) + VrChatFloatZeroCode;
        }

        internal static float DequantizeVrChatFloat(int code)
        {
            if (code < 0 || code >= VrChatFloatCardinality)
                throw new ArgumentOutOfRangeException(nameof(code));
            return (code - VrChatFloatZeroCode) / 127f;
        }

        private int[] EncodeEnumerativeCode(long code)
        {
            var digits = ParameterCompressionEnumerativeLayout.EncodeDigits(
                code, Alphabet.Radix, DigitCount);
            var words = new List<int>(FrameWordCount)
            {
                SpacerWord,
                Alphabet.SyncWord,
                SpacerWord
            };
            foreach (var digit in digits)
            {
                words.Add(Alphabet.EncodeDigit(digit));
                words.Add(SpacerWord);
            }
            words.Add(Alphabet.SyncWord);
            words.Add(SpacerWord);
            return words.ToArray();
        }
    }

    internal sealed class ParameterCompressionStreamingDecoder
    {
        private readonly ParameterCompressionProtocol protocol;
        private readonly List<int> digits = new List<int>();
        private int lastWireWord = int.MinValue;
        private bool spacerObserved;
        private bool collecting;
        private bool structurallyCorrupt;

        internal ParameterCompressionStreamingDecoder(
            ParameterCompressionProtocol protocol)
        {
            this.protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
        }

        internal bool TryPush(
            int wireWord,
            out ParameterCompressionDecodedValue decoded)
        {
            decoded = default;
            if (wireWord == lastWireWord) return false;
            lastWireWord = wireWord;

            if (wireWord == ParameterCompressionProtocol.SpacerWord)
            {
                spacerObserved = true;
                return false;
            }

            if (!protocol.Alphabet.IsCodeword(wireWord))
            {
                // Wrong-weight words are expected while individual Boolean wires
                // tear on their way to or from the all-zero spacer.
                return false;
            }

            if (!spacerObserved)
            {
                // A different valid word without an intervening zero violates the
                // framing contract. Repeated sampling was filtered above.
                if (collecting) structurallyCorrupt = true;
                return false;
            }
            spacerObserved = false;

            if (wireWord == protocol.Alphabet.SyncWord)
            {
                var committed = TryCommit(out decoded);
                collecting = true;
                structurallyCorrupt = false;
                digits.Clear();
                return committed;
            }

            if (!collecting) return false;
            if (!protocol.Alphabet.TryDecodeDigit(wireWord, out var digit))
            {
                structurallyCorrupt = true;
                return false;
            }
            if (digits.Count >= protocol.DigitCount)
            {
                structurallyCorrupt = true;
                return false;
            }
            digits.Add(digit);
            return false;
        }

        internal void Reset()
        {
            lastWireWord = int.MinValue;
            spacerObserved = false;
            collecting = false;
            structurallyCorrupt = false;
            digits.Clear();
        }

        private bool TryCommit(out ParameterCompressionDecodedValue decoded)
        {
            decoded = default;
            if (!collecting || structurallyCorrupt ||
                digits.Count != protocol.DigitCount)
                return false;
            var code = ParameterCompressionEnumerativeLayout.DecodeDigits(
                digits, protocol.Alphabet.Radix);
            if (!protocol.Layout.TryDecode(code, out var entry, out var valueCode))
                return false;
            decoded = new ParameterCompressionDecodedValue(
                entry.Name, entry.ChannelIndex, valueCode);
            return true;
        }
    }
}
