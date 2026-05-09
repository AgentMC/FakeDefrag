namespace DefragCore.UnitTests
{
    [TestClass]
    public sealed class VirtualFileTests
    {
        //Variants:
        // + space enough for all --> exception
        // + space enough for 0 clusters (0 to ^1 sectors) --> false, file unchanged
        // + segment does not belong to the file --> exception
        //   space enough for 1, n or ^1 cluster --> break
        //     segment to break may be singular, 1st, mid or last

        [TestMethod]
        [DataRow(0u, 0u)]
        [DataRow(1u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength)]
        [DataRow(1u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength + 1)]
        public void WhenSpaceIsEnoughForAll_ThrowsAndDoesNotChange(uint clusters, uint sectors)
        {
            var segment = new VirtualUnitSequence(0, clusters);
            var sut = new VirtualFile(string.Empty, 0, [segment]);
            Assert.ThrowsExactly<ArgumentException>(() => sut.TryBreakSegment(segment, sectors));
            Assert.HasCount(1, sut.ClusterSegmentsLocation);
            Assert.AreEqual(0ul, sut.ClusterSegmentsLocation[0].Address);
            Assert.AreEqual(clusters, sut.ClusterSegmentsLocation[0].NumUnits);
        }

        [TestMethod]
        [DataRow(1u, 0u)]
        [DataRow(1u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength - 1)]
        [DataRow(2u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength - 1)]
        public void WhenSpaceIsNotEnoughForASingleCluster_ReturnFalseAndDoesNotChange(uint clusters, uint sectors)
        {
            var segment = new VirtualUnitSequence(0, clusters);
            var sut = new VirtualFile(string.Empty, 0, [segment]);
            Assert.IsFalse(sut.TryBreakSegment(segment, sectors));
            Assert.HasCount(1, sut.ClusterSegmentsLocation);
            Assert.AreEqual(0ul, sut.ClusterSegmentsLocation[0].Address);
            Assert.AreEqual(clusters, sut.ClusterSegmentsLocation[0].NumUnits);
        }

        [TestMethod]
        public void WhenSegmentIsForeign_ThrowsAndDoesNotChange()
        {
            var segment = new VirtualUnitSequence(0, 2);
            var sut = new VirtualFile(string.Empty, 0, [new(0, 0)]);
            Assert.ThrowsExactly<ArgumentException>(() =>
                sut.TryBreakSegment(segment, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength));
            Assert.HasCount(1, sut.ClusterSegmentsLocation);
            Assert.AreEqual(0ul, sut.ClusterSegmentsLocation[0].Address);
            Assert.AreEqual(0u, sut.ClusterSegmentsLocation[0].NumUnits);
        }

        [TestMethod]
        [DataRow(1u, 0u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength, 1u)]
        [DataRow(1u, 0u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength * 3, 3u)]
        [DataRow(1u, 0u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength * 4, 4u)]
        [DataRow(2u, 0u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength, 1u)]
        [DataRow(2u, 0u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength * 3, 3u)]
        [DataRow(2u, 0u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength * 4, 4u)]
        [DataRow(3u, 1u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength, 1u)]
        [DataRow(3u, 1u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength * 3, 3u)]
        [DataRow(3u, 1u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength * 4, 4u)]
        [DataRow(3u, 2u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength, 1u)]
        [DataRow(3u, 2u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength * 3, 3u)]
        [DataRow(3u, 2u, 0ul, 5u, VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength * 4, 4u)]
        public void WhenSpaceIsEnough_BreakSegments(uint segmentCount, uint testSegmentPosition, ulong sAddress, uint sSizeClusters, uint breakSectors, uint sBrokenClusters)
        {
            var segment = new VirtualUnitSequence(sAddress, sSizeClusters);
            var segmentList = new List<VirtualUnitSequence>();
            for (uint i = 0; i < segmentCount; i++)
            {
                segmentList.Add(i == testSegmentPosition
                    ? segment
                    : new(153 * i + 14, i * i));
            }
            var sut = new VirtualFile(string.Empty, 0, segmentList);

            Assert.IsTrue(sut.TryBreakSegment(segment, breakSectors));

            Assert.HasCount((int)segmentCount + 1, sut.ClusterSegmentsLocation);
            int mod = 0;
            for (int i = 0; i < segmentCount; i++)
            {
                if (i == testSegmentPosition)
                {
                    mod = -1;
                    Assert.AreEqual(segment, sut.ClusterSegmentsLocation[i]);
                    Assert.AreEqual(sAddress, segment.Address);
                    Assert.AreEqual(sBrokenClusters, segment.NumUnits);
                    var newSegment = sut.ClusterSegmentsLocation[++i];
                    Assert.AreEqual(sBrokenClusters * VirtualFileSystem.ClusterSize, newSegment.Address);
                    Assert.AreEqual(sSizeClusters - sBrokenClusters, newSegment.NumUnits);
                }
                else
                {
                    var s = sut.ClusterSegmentsLocation[i];
                    Assert.AreEqual((ulong)(153 * (i + mod) + 14), s.Address);
                    Assert.AreEqual((uint)(i + mod) * (i + mod), s.NumUnits);
                }
            }
        }
    }
}
