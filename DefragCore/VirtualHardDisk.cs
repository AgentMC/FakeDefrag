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

        private readonly Dictionary<ulong, SectorState> DiskView = new();
        public readonly ulong SizeBytes;

        public ulong SpaceAllocated => (ulong)(SectorLength * DiskView.Count);
        public ulong SpaceAvailable => SizeBytes - SpaceAllocated;

        public bool InitComplete { get; set; } = false;

        public VirtualHardDisk(ulong sizeBytes)
        {
            SizeBytes = sizeBytes;
            for (ulong i = 0; i < (BootLoaderSize / SectorLength); i++)
            {
                DiskView[i] = SectorState.Locked;
            }
            var fatLocation = (ulong)Random.Shared.Next((int)(0.2 * SizeBytes), (int)(0.8 * sizeBytes)) / SectorLength;
            for (var i = fatLocation; i <= (2 * BootLoaderSize / SectorLength) - 1 + fatLocation; i++)
            {
                DiskView[i] = SectorState.Locked;
            }
        }

        public SectorState PeekAlignSector(ref ulong address)
        {
            if (address >= SizeBytes) throw new ArgumentOutOfRangeException(nameof(address), "Address must be less or equal than the disk size.");
            address -= address % SectorLength;
            return DiskView.TryGetValue(address / SectorLength, out var state) ? state : SectorState.Empty;
        }

        /// <summary>
        /// Attempts to find and return a data-free and usable (not Bad or Locked) location in memory defined by the request.
        /// </summary>
        /// <param name="request">The request defines the minimum address and the required space (in VHD Segments).</param>
        /// <returns>If the search according to the parameters is successful, returns the pointer to the memory location and identified free space.
        /// Returns null otherwise.</returns>
        public VirtualClusterSequence? FindNextFreeSpace(VirtualClusterSequence request)
        {
            var address = request.address;
            ulong? nextFreeAddress;
            while ((nextFreeAddress = FindNextFreeAddress(address)) != null)
            {
                address = nextFreeAddress.Value;
                var sector = address / SectorLength;
                var freeSpace = (uint)MeasureFreeSpaceInSectors(sector);
                if (freeSpace >= request.numUnits)
                {
                    return new(address, freeSpace);
                }
                address += (freeSpace + 1) * SectorLength;
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
            foreach (var sectorId in DiskView.Keys)
            {
                if (sectorId > startingSector &&
                    sectorId < maxSector &&
                    DiskView[sectorId] != SectorState.Empty)
                {
                    maxSector = sectorId;
                }
            }
            return maxSector - startingSector;
        }

        private void CheckConstraints(ulong address, ulong length, bool isRead)
        {
            if ((address + length) > SizeBytes) throw new ArgumentOutOfRangeException(nameof(address), "Address must be less or equal than the disk size lest the length.");
            if (address % SectorLength != 0) throw new ArgumentException("Address requested is not sector-aligned", nameof(address));
            if (InitComplete) Thread.Sleep((int)(length / (SectorLength * 1000) * (isRead ? SectorReadTimeUs : SectorWriteTimeUs)));
        }

        public void Read(ulong address, ulong length)
        {
            ReadBegin?.Invoke(this, new(address, length));
            CheckConstraints(address, length, true);
            if(InitComplete) Thread.Sleep((int)(length / (SectorLength * 1000) * SectorReadTimeUs));
            ReadEnd?.Invoke(this, new(address, length));
        }

        private void WriteInternal(ulong address, ulong length, Action<ulong> action)
        {
            WriteBegin?.Invoke(this, new(address, length));
            CheckConstraints(address, length, false);
            ulong location = address / SectorLength, writeSectors = (ulong)Math.Ceiling((double)length / SectorLength);
            for (var i = location; i <= writeSectors - 1 + location; i++)
            {
                if (DiskView.TryGetValue(i, out var state) && (state == SectorState.Locked || state == SectorState.Bad)) throw new InvalidOperationException($"Attempting to write to a sector {i} with invalid state {state}.");
                action(i);
            }
            WriteEnd?.Invoke(this, new(address, length));
        }

        public void Write(ulong address, ulong length)
        {
            WriteInternal(address, length, (i) => DiskView[i] = SectorState.Data);
        }
    
        public void Clear (ulong address, ulong length)
        {
            WriteInternal(address, length, (i) => DiskView.Remove(i));
        }

        public class DiskOperationEventArgs(ulong address, ulong length) : EventArgs
        {
            public ulong Address { get; } = address;
            public ulong Length { get; } = length;
        }

        public event EventHandler<DiskOperationEventArgs> ReadBegin, ReadEnd, WriteBegin, WriteEnd;
    }
}
