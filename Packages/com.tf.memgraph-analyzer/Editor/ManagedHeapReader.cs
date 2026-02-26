using System;

namespace Tools {
    public class ManagedHeapReader {
        private readonly SnapshotHeapSection[] _sections;
        private readonly int _pointerSize;

        // Cache for sorted section lookup
        private readonly ulong[] _sortedStartAddresses;
        private readonly int[] _sortedIndices;

        public ManagedHeapReader(SnapshotHeapSection[] sections, int pointerSize) {
            _sections = sections ?? Array.Empty<SnapshotHeapSection>();
            _pointerSize = pointerSize;

            // Build sorted index for binary search
            _sortedIndices = new int[_sections.Length];
            _sortedStartAddresses = new ulong[_sections.Length];
            for (int i = 0; i < _sections.Length; i++) {
                _sortedIndices[i] = i;
                _sortedStartAddresses[i] = _sections[i].StartAddress;
            }
            Array.Sort(_sortedStartAddresses, _sortedIndices);
        }

        public int PointerSize => _pointerSize;

        public bool TryReadPointer(ulong address, out ulong value) {
            value = 0;
            if (!TryFindSection(address, out int sectionIndex, out int offset))
                return false;

            var bytes = _sections[sectionIndex].Bytes;
            if (offset + _pointerSize > bytes.Length)
                return false;

            if (_pointerSize == 8)
                value = BitConverter.ToUInt64(bytes, offset);
            else if (_pointerSize == 4)
                value = BitConverter.ToUInt32(bytes, offset);
            else
                return false;

            return true;
        }

        public bool TryReadInt32(ulong address, out int value) {
            value = 0;
            if (!TryFindSection(address, out int sectionIndex, out int offset))
                return false;

            var bytes = _sections[sectionIndex].Bytes;
            if (offset + 4 > bytes.Length)
                return false;

            value = BitConverter.ToInt32(bytes, offset);
            return true;
        }

        public bool TryReadObjectTypePointer(ulong objectAddress, out ulong typeInfoAddress) {
            // Object header: [typeInfoPointer (pointerSize bytes)] [rest of header]
            // The type info pointer is at the very start of the object
            return TryReadPointer(objectAddress, out typeInfoAddress);
        }

        public bool TryReadBytes(ulong address, int length, out byte[] result) {
            result = null;
            if (!TryFindSection(address, out int sectionIndex, out int offset))
                return false;

            var bytes = _sections[sectionIndex].Bytes;
            if (offset + length > bytes.Length)
                return false;

            result = new byte[length];
            Buffer.BlockCopy(bytes, offset, result, 0, length);
            return true;
        }

        public bool IsValidAddress(ulong address) {
            return TryFindSection(address, out _, out _);
        }

        public bool TryFindSection(ulong address, out int sectionIndex, out int offset) {
            sectionIndex = -1;
            offset = 0;

            if (_sections.Length == 0)
                return false;

            // Binary search for the section containing this address
            int lo = 0, hi = _sortedStartAddresses.Length - 1;
            int candidate = -1;

            while (lo <= hi) {
                int mid = lo + (hi - lo) / 2;
                if (_sortedStartAddresses[mid] <= address) {
                    candidate = mid;
                    lo = mid + 1;
                } else {
                    hi = mid - 1;
                }
            }

            if (candidate < 0)
                return false;

            int realIndex = _sortedIndices[candidate];
            var section = _sections[realIndex];
            ulong sectionEnd = section.StartAddress + (ulong)section.Bytes.Length;

            if (address >= section.StartAddress && address < sectionEnd) {
                sectionIndex = realIndex;
                offset = (int)(address - section.StartAddress);
                return true;
            }

            return false;
        }
    }
}
