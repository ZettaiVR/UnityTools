using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Profiling;

namespace Zettai
{
    /// <summary>
    /// Create a stream reader to decrypt CVR assets using v1 encryption
    /// </summary>
    public class CvrEncryptedStream : Stream
    {
        private readonly Stream stream;
        private readonly byte[] keyFragment;
        private readonly int streamLength;
        private readonly int fragmentSize;
        private readonly List<FastCVRDecrypt.CopyEvent> copyEvents = new List<FastCVRDecrypt.CopyEvent>(100);
        private readonly List<int> destStart = new List<int>(100);

        static readonly ProfilerMarker s_ReadPerfMarker = new ProfilerMarker("Zettai.CvrStream.ReadData");

        public CvrEncryptedStream(string guid, byte[] data, byte[] keyFrag)
            : this(guid, new MemoryStream(data, false), keyFrag) { }
        public CvrEncryptedStream(string guid, byte[] data, string keyFragBase64)
            : this(guid, new MemoryStream(data, false), Convert.FromBase64String(keyFragBase64)) { }
        public CvrEncryptedStream(string guid, Stream originalStream, string keyFragBase64)
            : this(guid, originalStream, Convert.FromBase64String(keyFragBase64)) { }
        public CvrEncryptedStream(string guid, Stream originalStream, byte[] keyFrag)
        {
            if (string.IsNullOrEmpty(guid))
                throw new ArgumentNullException(nameof(guid));
            if (keyFrag == null)
                throw new ArgumentNullException(nameof(keyFrag));
            if (originalStream == null)
                throw new ArgumentNullException(nameof(originalStream));
            if (keyFrag.Length == 0)
                throw new ArgumentOutOfRangeException(nameof(keyFrag));

            stream = originalStream;
            keyFragment = keyFrag;
            var streamLength = (int)originalStream.Length;
            this.streamLength = streamLength + keyFragment.Length;
            Span<byte> guidBytes = stackalloc byte[36];
            for (int i = 0; i < guid.Length; i++)
                guidBytes[i] = (byte)guid[i];
            FastCVRDecrypt.DecryptData(guidBytes, streamLength, keyFragment, copyEvents, destStart);
            fragmentSize = this.streamLength / destStart.Count;
        }       
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Position = offset;
                    break;
                case SeekOrigin.Current:
                    Position += offset;
                    break;
                case SeekOrigin.End:
                    Position = Length + offset;
                    break;
            }
            return Position;
        }
        public override void SetLength(long value) { }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) { }       
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => streamLength;
        public override long Position { get; set; }
        public override int Read(Span<byte> buffer)
        {
            s_ReadPerfMarker.Begin();
            int remainingBytes = buffer.Length;
            int totalReadBytes = 0;
            while (remainingBytes > 0)
            {
                var block = GetStartIndex((int)Position + totalReadBytes, remainingBytes);
                if (!block.isValid)
                    break;

                int fileReadStart = block.Start;
                int bytesCanRead = Math.Min(remainingBytes, block.Length);
                int readBytes;
                if (block.isKey)
                {
                    keyFragment.AsSpan(fileReadStart, bytesCanRead).CopyTo(buffer.Slice(totalReadBytes, bytesCanRead));
                    readBytes = bytesCanRead;
                }
                else
                {
                    stream.Position = fileReadStart;
                    readBytes = stream.Read(buffer.Slice(totalReadBytes, bytesCanRead));
                }
                totalReadBytes += readBytes;
                remainingBytes -= readBytes;
                if (readBytes == 0)
                    break;
            }
            Position += totalReadBytes;
            s_ReadPerfMarker.End();
            return totalReadBytes;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }
        public override int ReadByte()
        {
            Span<byte> buffer = stackalloc byte[1];
            var read = Read(buffer);
            if (read == 1)
                return buffer[0];
            return read;
        }
        public byte[] ReadAll() 
        {
            var outBytes = new byte[streamLength];
            Read(outBytes, 0, streamLength);
            return outBytes;        
        }       
        private int FastSearch(int requestedStart) 
        {
            if (requestedStart == 0)
                return 0;
            var guess = requestedStart / fragmentSize;
            var count = destStart.Count;
            if (guess >= count)
                return -1;
            var startValue = destStart[guess];
            var nextValue = count > guess + 1 ? destStart[guess + 1] : startValue + fragmentSize * 2;
            if (startValue <= requestedStart && nextValue > requestedStart)
                return guess;
            if (startValue > requestedStart)
            {
                for (int i = guess - 1; i >= 0; i--)
                {
                    startValue = destStart[i];
                    nextValue = destStart[i + 1];
                    if (startValue <= requestedStart && nextValue > requestedStart)
                        return i;
                }
            }
            else 
            {
                for (int i = guess + 1; i < count; i++)
                {
                    startValue = destStart[i];
                    nextValue = count > i + 1 ? destStart[i + 1] : startValue + fragmentSize * 2;
                    if (startValue <= requestedStart && nextValue > requestedStart)
                        return i;
                }
            }
            return -1;
        }
        private ReadIndex GetStartIndex(int requestedStart, int length)
        {
            if (requestedStart >= streamLength)
                return new ReadIndex();
            var index = FastSearch(requestedStart);
            if (index >= 0)
            {
                var item = copyEvents[index];
                if (IsMatch(ref item, requestedStart, length, out int blockLength, out int fileOffset))
                    return new ReadIndex(fileOffset, blockLength, true, item.sourceIsKey);
            }
            // search should find it, but if not, we can go through all events
            var count = copyEvents.Count;
            for (int i = 0; i < count; i++)
            {
                var item = copyEvents[i];
                if (!IsMatch(ref item, requestedStart, length, out int blockLength, out int fileOffset))
                    continue;
                return new ReadIndex(fileOffset, blockLength, true, item.sourceIsKey);
            }
            return new ReadIndex();
        }
        private static bool IsMatch(ref FastCVRDecrypt.CopyEvent item, int requestedStart, int length, out int blockLength, out int fileOffset)         
        {
            var start = item.destStart;
            var end = start + item.length;
            var diff = requestedStart - item.destStart;
            fileOffset = item.sourceStart + diff;
            blockLength = Math.Min(length, item.length - (requestedStart - start));
            return start <= requestedStart && requestedStart < end;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct ReadIndex
        {
            public int Start;
            public int Length;
            public bool isValid;
            public bool isKey;
            public ReadIndex(int start, int length, bool valid, bool key)
            {
                Start = start;
                Length = length;
                isValid = valid;
                isKey = key;
            }
        }
        public class FastCVRDecrypt
        {
            static readonly ProfilerMarker s_DecryptPerfMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Zettai.CvrStream.DecryptBuild");
            public struct Step
            {
                public long start;

                public int length;
                public Step(long Start, long Length)
                {
                    start = Start;
                    length = (int)Length;
                }
            }
            public struct Copies
            {
                public CopyEvent first;
                public CopyEvent key;
                public bool hasKey;
                public Copies(CopyEvent first)
                {
                    this.first = first;
                    key = default;
                    hasKey = false;
                }
                public Copies(CopyEvent first, CopyEvent second)
                {
                    this.first = first;
                    this.key = second;
                    hasKey = true;
                }
            }
            public struct CopyEvent
            {
                public int sourceStart;
                public int length;
                public bool sourceIsKey;
                public int destStart;

                public CopyEvent(int sourceStart, int length, bool sourceIsKey)
                {
                    this.sourceStart = sourceStart;
                    this.length = length;
                    this.sourceIsKey = sourceIsKey;
                    destStart = 0;
                }
            }
            private const long RANDOM_START = 0x3FFFFFEFFFFFFF;
            private static long Rand(long randStart, uint crc, uint fragSize)
            {
                return (randStart * crc + randStart) % (long)fragSize + fragSize;
            }
            public static void DecryptData(Span<byte> guidBytes, int originalLength, byte[] keyFrag, List<CopyEvent> copies, List<int> destStart)
            {
                s_DecryptPerfMarker.Begin();
                var randStart = RANDOM_START;
                var crc = Crc32Algorithm.Compute(guidBytes);
                var newLength = originalLength + 1000;
                var fragSize = (uint)Math.Max(newLength / 100, 1000); // at most 100 fragments, at least 1000 bytes big
                long pos = 0L;
                int stepCount = 0;
                Span<Step> stepSpan = stackalloc Step[128];
                while (pos < newLength)
                {
                    long prnd = randStart = Rand(randStart, crc, fragSize);
                    if (prnd + pos > newLength)
                        prnd = newLength - pos;

                    stepSpan[stepCount] = new Step(pos, prnd);
                    pos += prnd;
                    stepCount++;
                }
                stepSpan = stepSpan.Slice(0, stepCount);
                Span<int> ints = stackalloc int[stepCount];
                for (int i = 0; i < stepCount; i++)
                {
                    ints[i] = i;
                }
                for (int i = 1; i < stepCount; i++)
                {
                    randStart = Rand(randStart, crc, fragSize);
                    int index = (int)(randStart % (stepCount - 1) + 1);
                    int temp = ints[index];
                    ints[index] = ints[i];
                    ints[i] = temp;
                }
                Span<Copies> copyArray = stackalloc Copies[stepCount];
                uint offset = 0u;
                for (int i = 0; i < stepCount; i++)
                {
                    var fragmentLength = stepSpan[ints[i]].length;
                    if ((offset + fragmentLength) < originalLength)
                    {
                        var copyEvent = new CopyEvent((int)offset, fragmentLength, false);
                        copyArray[ints[i]] = new Copies(copyEvent);
                    }
                    else
                    {
                        if (offset <= originalLength)
                        {
                            var inOriginal = (int)(originalLength - offset);
                            var keyLength = fragmentLength - inOriginal;
                            var copyEvent1 = new CopyEvent((int)offset, inOriginal, false);
                            var copyEvent2 = new CopyEvent(0, keyLength, true);
                            copyArray[ints[i]] = new Copies(copyEvent1, copyEvent2);
                        }
                        else
                        {
                            // afaik never happens
                            var fragOffset = (int)(offset - originalLength);
                            var copyEvent = new CopyEvent(fragOffset, fragmentLength, true);
                            copyArray[ints[i]] = new Copies(copyEvent);
                        }
                    }
                    offset += (uint)stepSpan[ints[i]].length;
                }
                copies.Clear();
                int length = 0;
                for (int i = 0; i < stepCount; i++)
                {
                    var copyElement = copyArray[i];
                    copyElement.first.destStart = length;
                    destStart.Add(length);
                    length += copyElement.first.length;
                    copies.Add(copyElement.first);
                    if (copyElement.hasKey)
                    {
                        copyElement.key.destStart = length;
                        copies.Add(copyElement.key);
                        destStart.Add(length);
                        length += copyElement.key.length;
                    }
                }
                s_DecryptPerfMarker.End();
            }
        }
        public class Crc32Algorithm
        {
            public static uint Append(uint initial, Span<byte> input)
            {
                if (input == null)
                    throw new ArgumentNullException();
                return AppendInternal(initial, input, 0, input.Length);
            }
            public static uint Compute(Span<byte> input)
            {
                return Append(0, input);
            }
            private static readonly SafeProxy _proxy = new SafeProxy();
            private static uint AppendInternal(uint initial, Span<byte> input, int offset, int length)
            {
                if (length > 0)
                {
                    return _proxy.Append(initial, input, offset, length);
                }
                else
                    return initial;
            }

            private class SafeProxy
            {
                private const uint Poly = 0xedb88320u;

                private readonly uint[] _table = new uint[16 * 256];

                internal SafeProxy()
                {
                    Init(Poly);
                }

                protected void Init(uint poly)
                {
                    var table = _table;
                    for (uint i = 0; i < 256; i++)
                    {
                        uint res = i;
                        for (int t = 0; t < 16; t++)
                        {
                            for (int k = 0; k < 8; k++) res = (res & 1) == 1 ? poly ^ (res >> 1) : (res >> 1);
                            table[(t * 256) + i] = res;
                        }
                    }
                }

                public uint Append(uint crc, Span<byte> input, int offset, int length)
                {
                    uint crcLocal = uint.MaxValue ^ crc;

                    uint[] table = _table;
                    while (length >= 16)
                    {
                        var a = table[(3 * 256) + input[offset + 12]]
                            ^ table[(2 * 256) + input[offset + 13]]
                            ^ table[(1 * 256) + input[offset + 14]]
                            ^ table[(0 * 256) + input[offset + 15]];

                        var b = table[(7 * 256) + input[offset + 8]]
                            ^ table[(6 * 256) + input[offset + 9]]
                            ^ table[(5 * 256) + input[offset + 10]]
                            ^ table[(4 * 256) + input[offset + 11]];

                        var c = table[(11 * 256) + input[offset + 4]]
                            ^ table[(10 * 256) + input[offset + 5]]
                            ^ table[(9 * 256) + input[offset + 6]]
                            ^ table[(8 * 256) + input[offset + 7]];

                        var d = table[(15 * 256) + ((byte)crcLocal ^ input[offset])]
                            ^ table[(14 * 256) + ((byte)(crcLocal >> 8) ^ input[offset + 1])]
                            ^ table[(13 * 256) + ((byte)(crcLocal >> 16) ^ input[offset + 2])]
                            ^ table[(12 * 256) + ((crcLocal >> 24) ^ input[offset + 3])];

                        crcLocal = d ^ c ^ b ^ a;
                        offset += 16;
                        length -= 16;
                    }

                    while (--length >= 0)
                        crcLocal = table[(byte)(crcLocal ^ input[offset++])] ^ crcLocal >> 8;

                    return crcLocal ^ uint.MaxValue;
                }
            }
        }
    }
}
