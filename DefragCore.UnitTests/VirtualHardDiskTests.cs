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
    //Read
    //Write
    //Clear
    //read/write events
    //read/write delay
}
