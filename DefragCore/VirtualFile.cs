namespace DefragCore
{
    /// <summary>
    /// File Descriptor
    /// </summary>
    /// <param name="Name">Full path, name and extension</param>
    /// <param name="Size">Size in bytes</param>
    /// <param name="ClusterSegmentsLocation">Physical memory pointers. Units are FS clusters.</param>
    public record VirtualFile(string Name, ulong Size, List<VirtualUnitSequence> ClusterSegmentsLocation)
    {
        public bool TryBreakSegment(VirtualUnitSequence segmentOfClusters, uint availableSectors)
        {
            var movableClusters = availableSectors * VirtualHardDisk.SectorLength / VirtualFileSystem.ClusterSize;//we can only move this many clusters
            if (movableClusters >= segmentOfClusters.NumUnits) throw new ArgumentException("Available sectors are enough to contain the whole segment. Breaking is not required.", nameof(availableSectors));
            if (movableClusters == 0) return false;  //Unable to move a single solid cluster.
            var differenceInClusters = segmentOfClusters.NumUnits - movableClusters;//actual unmovable difference to break
            var segmentIndex = ClusterSegmentsLocation.IndexOf(segmentOfClusters);
            if (segmentIndex == -1) throw new ArgumentException("Segment does not belong to this file.", nameof(segmentOfClusters));
            segmentOfClusters.NumUnits = movableClusters;              //actually break the segment
            ClusterSegmentsLocation.Insert(segmentIndex + 1, new(segmentOfClusters.Address + segmentOfClusters.NumUnits * VirtualFileSystem.ClusterSize, differenceInClusters));
            return true;
        }
    }
}
