using static DefragCore.VirtualHardDisk;

namespace DefragCore.UnitTests;

[TestClass]
public class VirtualHardDiskTests
{
    private const ulong FourMB = 4 * 1024 * 1024;

    [TestMethod]
    public void ConstructorPublic_SetsStartingProperties()
    {
        var sut = new VirtualHardDisk(FourMB);
        Assert.AreEqual(FourMB, sut.SizeBytes);
        Assert.IsFalse(sut.SimulateIoDelay);
        var type = (SectorState)(-1);
        Dictionary<SectorState, int> counter = new()
        {
            {SectorState.Empty,0 },
            {SectorState.Data,0  },
            {SectorState.Locked,0  },
            {SectorState.Bad,0  }
        };
        for (ulong ptr = 0; ptr < FourMB; ptr += SectorLength)
        {
            var sectorType = sut.PeekAlignSector(ref ptr);
            if (sectorType != type)
            {
                type = sectorType;
                counter[type] += 1;
            }
        }
        Assert.AreEqual(2, counter[SectorState.Empty]);
        Assert.AreEqual(0, counter[SectorState.Data]);
        Assert.AreEqual(2, counter[SectorState.Locked]);
        Assert.AreEqual(0, counter[SectorState.Bad]);
    }

    [TestMethod]
    public void ConstructorInternal_AcceptsParametersAsPassed()
    {
        ulong sizeBytes = (ulong)Random.Shared.Next(1024, int.MaxValue);
        ulong address = (ulong)Random.Shared.Next((int)sizeBytes);
        ulong sectorId = address / SectorLength;
        SectorState state = (SectorState)Random.Shared.Next(4);
        var dv = new Dictionary<ulong, SectorState> { { sectorId, state }, };
        var sut = new VirtualHardDisk(dv, sizeBytes, true);

        Assert.AreEqual(sizeBytes, sut.SizeBytes);
        Assert.IsTrue(sut.SimulateIoDelay);
        Assert.AreEqual(state, sut.PeekAlignSector(ref address));
    }

    [TestMethod]
    public void SizeCalculationProperties_ReturnRightValues()
    {
        ulong sizeBytes = (ulong)Random.Shared.Next(1024, int.MaxValue);
        var sectorCount = (ulong)Random.Shared.Next(10, 20);
        var dv = new Dictionary<ulong, SectorState>();
        for (ulong i = 0; i < sectorCount; i++)
        {
            dv.Add(i, SectorState.Data);
        }
        var sut = new VirtualHardDisk(dv, sizeBytes, false);
        var expectedAlloc = sectorCount * SectorLength;
        Assert.AreEqual(expectedAlloc, sut.SpaceAllocated);
        Assert.AreEqual(sizeBytes - expectedAlloc, sut.SpaceAvailable);
    }

    //PeakAlignSector:
    //1. throws on >= sizeBytes
    //2. adjusts the pointer to be at 0th byte of the sector
    //3. if sector pointed at is registered - returns state
    //4. else returns empty
    [TestMethod]
    public void PeekAlignSector_ValidatesPointer()
    {
        ulong address = 100, test = address;
        var sut = new VirtualHardDisk([], address, false);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sut.PeekAlignSector(ref address));
        Assert.AreEqual(test, address);
        address++;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sut.PeekAlignSector(ref address));
        Assert.AreEqual(++test, address);
    }

    [TestMethod]
    public void PeekAlignSector_AdjustsPointer()
    {
        ulong address = 100;
        var sut = new VirtualHardDisk([], 129, false);
        sut.PeekAlignSector(ref address);
        Assert.AreEqual(0u, address);
        address = SectorLength;
        sut.PeekAlignSector(ref address);
        Assert.AreEqual(SectorLength, address);
    }

    [TestMethod]
    public void PeekAlignSector_ReturnsEmptyIfSectorsAreNotRegisteredAndDataOtherwise()
    {
        const int testMax = 10;
        var sectors = Enumerable.Range(0, testMax).ToList();
        for (int i = 0; i < testMax/2; i++)
        {
            sectors.RemoveAt(Random.Shared.Next(sectors.Count));
        }
        var dv = sectors.ToDictionary(id => (ulong)id, id => SectorState.Data);
        var sut = new VirtualHardDisk(dv, testMax * SectorLength, false);
        foreach(var sectorId in Enumerable.Range(0, testMax))
        {
            var expectedState = sectors.Contains(sectorId) ? SectorState.Data : SectorState.Empty;
            var address = (ulong)sectorId * SectorLength;
            Assert.AreEqual(expectedState, sut.PeekAlignSector(ref address));
        }
    }

    //todo:
    //FindNextFreeSectorSpace
    //  + address > size --> null
    //  + point at mid-empty sector --> next sector
    //  + point at mid-empty sector before data sector --> next sector after data sector
    //  + point at start empty sector --> this sector
    //  + point at single data sector --> next sector
    //      + single empty --> 1 length
    //      + n empty --> n length
    //      + n last empty --> n length
    //          + skips free area less than requested
    //  + point at mid-last sector --> null
    //  + point at last data sector --> null
    //  + point at start last empty sector --> this sector, 1 length
    //  + point at start n-last empty sector --> this sector, n length
    [TestMethod]
    public void FindNextFreeSectorSpace_ReturnsNullIfPointerOurOfRange()
    {
        var sut = new VirtualHardDisk([], 1024, false);
        Assert.IsNull(sut.FindNextFreeSectorSpace(new(1024, 0)));
        Assert.IsNull(sut.FindNextFreeSectorSpace(new(1025, 0)));
        Assert.IsNull(sut.FindNextFreeSectorSpace(new(1024 + SectorLength, 0)));
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_SkipsForwardOnMidEmptySector()
    {
        var sut = new VirtualHardDisk([], 1024, false);
        var result = sut.FindNextFreeSectorSpace(new(SectorLength / 2, 1));
        Assert.AreEqual(SectorLength, result.Address);
        Assert.AreEqual(1024 / SectorLength - 1, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_SkipsForwardOnMidEmptySectorAndData()
    {
        var sut = new VirtualHardDisk(new() { { 1, SectorState.Data } }, 1024, false);
        var result = sut.FindNextFreeSectorSpace(new(SectorLength / 2, 1));
        Assert.AreEqual(SectorLength * 2, result.Address);
        Assert.AreEqual(1024 / SectorLength - 2, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartEmptySectorReturnsThisSector()
    {
        var sut = new VirtualHardDisk([], 1024, false);
        var result = sut.FindNextFreeSectorSpace(new(0, 1));
        Assert.AreEqual(0u, result.Address);
        Assert.AreEqual(1024 / SectorLength, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartDataSectorReturnsNextSector()
    {
        var sut = new VirtualHardDisk(new() { { 0, SectorState.Data } }, 1024, false);
        var result = sut.FindNextFreeSectorSpace(new(0, 1));
        Assert.AreEqual(SectorLength, result.Address);
        Assert.AreEqual(1024 / SectorLength - 1, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartDataSectorReturnsNextSectorSingular()
    {
        var sut = new VirtualHardDisk(new() { { 0, SectorState.Locked }, {2, SectorState.Bad } }, 1024, false);
        var result = sut.FindNextFreeSectorSpace(new(0, 1));
        Assert.AreEqual(SectorLength, result.Address);
        Assert.AreEqual(1u, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartDataSectorReturnsNextSectorMulti()
    {
        var sut = new VirtualHardDisk(new() { { 1, SectorState.Locked }, {5, SectorState.Bad } }, 1024, false);
        var result = sut.FindNextFreeSectorSpace(new(SectorLength, 1));
        Assert.AreEqual(SectorLength * 2, result.Address);
        Assert.AreEqual(3u, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartDataSectorReturnsNextSectorMultiAsRequired()
    {
        var sut = new VirtualHardDisk(new() { { 1, SectorState.Locked }, {3, SectorState.Bad } }, 1024, false);
        var result = sut.FindNextFreeSectorSpace(new(SectorLength, 4));
        Assert.AreEqual(SectorLength * 4, result.Address);
        Assert.AreEqual(4u, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtMidLastSectorReturnsNull()
    {
        var sut = new VirtualHardDisk([], 1024, false);
        Assert.IsNull(sut.FindNextFreeSectorSpace(new((ulong)(SectorLength * 7.5), 1)));
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartLastDataSectorReturnsNull()
    {
        var sut = new VirtualHardDisk(new() { { 7, SectorState.Data } }, 1024, false);
        Assert.IsNull(sut.FindNextFreeSectorSpace(new(SectorLength * 7, 1)));
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartLastSectorReturnsOneSector()
    {
        var sut = new VirtualHardDisk([], 1024, false);
        var result = sut.FindNextFreeSectorSpace(new(SectorLength * 7, 1));
        Assert.AreEqual(SectorLength * 7, result.Address);
        Assert.AreEqual(1u, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartLastNthSectorReturnsNSectors()
    {
        var sut = new VirtualHardDisk([], 1024, false);
        var result = sut.FindNextFreeSectorSpace(new(SectorLength * 6, 1));
        Assert.AreEqual(SectorLength * 6, result.Address);
        Assert.AreEqual(2u, result.NumUnits);
    }

    //Read
    //Write
    //Clear
    //read/write events
    //read/write delay
}
