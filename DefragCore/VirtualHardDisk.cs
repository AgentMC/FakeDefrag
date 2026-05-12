namespace DefragCore
{
    public class VirtualHardDisk
    {
        public const uint SectorLength = 128,
                          BootLoaderSize = 512 * 1024;

        private const uint SectorReadTimeUs = 150,
                           SectorWriteTimeUs = 200;
                   
        
        public enum SectorState
        {
            Empty, Data, Locked, Bad
        }

        private readonly Dictionary<ulong, SectorState> _diskView;

        public readonly ulong SizeBytes;

        public ulong SpaceAllocated => (ulong)(SectorLength * _diskView.Count);
        public ulong SpaceAvailable => SizeBytes - SpaceAllocated;

        public bool SimulateIoDelay { get; set; } 

        internal VirtualHardDisk(Dictionary<ulong, SectorState> diskView, ulong sizeBytes, bool simlulateIoDelay)
        {
            _diskView = diskView;
            SizeBytes = sizeBytes;
            SimulateIoDelay = simlulateIoDelay;
        }

        public VirtualHardDisk(ulong sizeBytes) : this([], sizeBytes, false)
        {
            for (ulong i = 0; i < (BootLoaderSize / SectorLength); i++)
            {
                _diskView[i] = SectorState.Locked;
            }
            var fatLocation = (ulong)Random.Shared.Next((int)(0.2 * SizeBytes), (int)(0.8 * sizeBytes)) / SectorLength;
            for (var i = fatLocation; i <= (2 * BootLoaderSize / SectorLength) - 1 + fatLocation; i++)
            {
                _diskView[i] = SectorState.Locked;
            }
        }

        public SectorState PeekAlignSector(ref ulong address)
        {
            if (address >= SizeBytes) throw new ArgumentOutOfRangeException(nameof(address), "Address must be less or equal than the disk size.");
            address -= address % SectorLength;
            return _diskView.TryGetValue(address / SectorLength, out var state) ? state : SectorState.Empty;
        }

        /// <summary>
        /// Attempts to find and return a data-free and usable (not Bad or Locked) location in memory defined by the request.
        /// </summary>
        /// <param name="freeSectorRequest">The request defines the minimum address and the required space (in VHD Segments).</param>
        /// <returns>If the search according to the parameters is successful, returns the pointer to the memory location and identified free space.
        /// Returns null otherwise.</returns>
        public VirtualUnitSequence? FindNextFreeSectorSpace(VirtualUnitSequence freeSectorRequest)
        {
            var address = freeSectorRequest.Address;
            ulong? nextFreeAddress;
            while ((nextFreeAddress = FindNextFreeAddress(address)) != null)
            {
                if(nextFreeAddress >= address)
                {
                    address = nextFreeAddress.Value;
                    var sector = address / SectorLength;
                    var freeSpaceInSectors = (uint)MeasureFreeSpaceInSectors(sector);
                    if (freeSpaceInSectors >= freeSectorRequest.NumUnits)
                    {
                        return new(address, freeSpaceInSectors);
                    }
                    address += (freeSpaceInSectors + 1) * SectorLength;
                }
                else // mid-free sector case
                {
                    address = nextFreeAddress.Value + SectorLength;
                }
            }
            return null;
        }

        private ulong? FindNextFreeAddress(ulong startingAddress)
        {
            ulong address = startingAddress;
            while (address < SizeBytes)
            {
                if (PeekAlignSector(ref address) == SectorState.Empty)
                {
                    return address;
                }
                else
                {
                    address += SectorLength;
                }
            }
            return null;
        }

        private ulong MeasureFreeSpaceInSectors(ulong startingSector)
        {
            ulong maxSector = SizeBytes / SectorLength;
            foreach (var sector in _diskView)
            {
                if (sector.Key > startingSector &&
                    sector.Key < maxSector &&
                    sector.Value != SectorState.Empty)
                {
                    maxSector = sector.Key;
                }
            }
            return maxSector - startingSector;
        }

        private void CheckConstraints(ulong address, ulong length, bool isRead)
        {
            if (length == 0) throw new ArgumentException("Length cannot be 0", nameof(length));
            if (address + length > SizeBytes) throw new ArgumentOutOfRangeException(nameof(address), "Address must be less or equal than the disk size lest the length.");
            if (address % SectorLength != 0) throw new ArgumentException("Address requested is not sector-aligned", nameof(address));
            if (SimulateIoDelay) Thread.Sleep((int)(length / (SectorLength * 1000) * (isRead ? SectorReadTimeUs : SectorWriteTimeUs)));
        }

        public void Read(ulong address, ulong length)
        {
            ReadBegin?.Invoke(this, new(address, length));
            CheckConstraints(address, length, true);
            if(SimulateIoDelay) Thread.Sleep((int)(length / (SectorLength * 1000) * SectorReadTimeUs));
            ReadEnd?.Invoke(this, new(address, length));
        }

        private void WriteInternal(ulong address, ulong length, Action<ulong> action)
        {
            WriteBegin?.Invoke(this, new(address, length));
            CheckConstraints(address, length, false);
            ulong location = address / SectorLength, writeSectors = (ulong)Math.Ceiling((double)length / SectorLength);
            for (var i = location; i <= writeSectors - 1 + location; i++)
            {
                if (_diskView.TryGetValue(i, out var state) && (state == SectorState.Locked || state == SectorState.Bad)) throw new InvalidOperationException($"Attempting to write to a sector {i} with invalid state {state}.");
                action(i);
            }
            WriteEnd?.Invoke(this, new(address, length));
        }

        public void Write(ulong address, ulong length)
        {
            WriteInternal(address, length, (i) => _diskView[i] = SectorState.Data);
        }
    
        public void Clear (ulong address, ulong length)
        {
            WriteInternal(address, length, (i) => _diskView.Remove(i));
        }

        public class DiskOperationEventArgs(ulong address, ulong length) : EventArgs
        {
            public ulong Address { get; } = address;
            public ulong Length { get; } = length;
        }

        public event EventHandler<DiskOperationEventArgs>? ReadBegin, ReadEnd, WriteBegin, WriteEnd;
    }
}
