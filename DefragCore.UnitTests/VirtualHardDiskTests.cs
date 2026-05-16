using System.Diagnostics;
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

    private static VirtualHardDisk GetVhd1024False(Dictionary<ulong, SectorState>? contents = null) => new VirtualHardDisk(contents ?? [], 1024, false);

    [TestMethod]
    public void FindNextFreeSectorSpace_ReturnsNullIfPointerOurOfRange()
    {
        var sut = GetVhd1024False();
        Assert.IsNull(sut.FindNextFreeSectorSpace(new(1024, 0)));
        Assert.IsNull(sut.FindNextFreeSectorSpace(new(1025, 0)));
        Assert.IsNull(sut.FindNextFreeSectorSpace(new(1024 + SectorLength, 0)));
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_SkipsForwardOnMidEmptySector()
    {
        var sut = GetVhd1024False();
        var result = sut.FindNextFreeSectorSpace(new(SectorLength / 2, 1));
        Assert.AreEqual(SectorLength, result.Address);
        Assert.AreEqual(1024 / SectorLength - 1, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_SkipsForwardOnMidEmptySectorAndData()
    {
        var sut = GetVhd1024False(new() { { 1, SectorState.Data } });
        var result = sut.FindNextFreeSectorSpace(new(SectorLength / 2, 1));
        Assert.AreEqual(SectorLength * 2, result.Address);
        Assert.AreEqual(1024 / SectorLength - 2, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartEmptySectorReturnsThisSector()
    {
        var sut = GetVhd1024False();
        var result = sut.FindNextFreeSectorSpace(new(0, 1));
        Assert.AreEqual(0u, result.Address);
        Assert.AreEqual(1024 / SectorLength, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartDataSectorReturnsNextSector()
    {
        var sut = GetVhd1024False(new() { { 0, SectorState.Data } });
        var result = sut.FindNextFreeSectorSpace(new(0, 1));
        Assert.AreEqual(SectorLength, result.Address);
        Assert.AreEqual(1024 / SectorLength - 1, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartDataSectorReturnsNextSectorSingular()
    {
        var sut = GetVhd1024False(new() { { 0, SectorState.Locked }, {2, SectorState.Bad } });
        var result = sut.FindNextFreeSectorSpace(new(0, 1));
        Assert.AreEqual(SectorLength, result.Address);
        Assert.AreEqual(1u, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartDataSectorReturnsNextSectorMulti()
    {
        var sut = GetVhd1024False(new() { { 1, SectorState.Locked }, {5, SectorState.Bad } });
        var result = sut.FindNextFreeSectorSpace(new(SectorLength, 1));
        Assert.AreEqual(SectorLength * 2, result.Address);
        Assert.AreEqual(3u, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartDataSectorReturnsNextSectorMultiAsRequired()
    {
        var sut = GetVhd1024False(new() { { 1, SectorState.Locked }, {3, SectorState.Bad } });
        var result = sut.FindNextFreeSectorSpace(new(SectorLength, 4));
        Assert.AreEqual(SectorLength * 4, result.Address);
        Assert.AreEqual(4u, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtMidLastSectorReturnsNull()
    {
        var sut = GetVhd1024False();
        Assert.IsNull(sut.FindNextFreeSectorSpace(new((ulong)(SectorLength * 7.5), 1)));
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartLastDataSectorReturnsNull()
    {
        var sut = GetVhd1024False(new() { { 7, SectorState.Data } });
        Assert.IsNull(sut.FindNextFreeSectorSpace(new(SectorLength * 7, 1)));
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartLastSectorReturnsOneSector()
    {
        var sut = GetVhd1024False();
        var result = sut.FindNextFreeSectorSpace(new(SectorLength * 7, 1));
        Assert.AreEqual(SectorLength * 7, result.Address);
        Assert.AreEqual(1u, result.NumUnits);
    }

    [TestMethod]
    public void FindNextFreeSectorSpace_AtStartLastNthSectorReturnsNSectors()
    {
        var sut = GetVhd1024False();
        var result = sut.FindNextFreeSectorSpace(new(SectorLength * 6, 1));
        Assert.AreEqual(SectorLength * 6, result.Address);
        Assert.AreEqual(2u, result.NumUnits);
    }

    //Read
    //+read start event
    //+delay conditional
    //+read end event on success
    //+constraints
    // +Length 0 --> Argument Exc
    // +out of bounds by address or address + length --> Arg out of Range
    // +address misaligned
    // +no read end event on failure
    //+valid bounds tests
    [TestMethod]
    public void Read_RaisesBeginEvent()
    {
        bool eventRaised = false;
        object? eventO = null;
        DiskOperationEventArgs? eventE = null;
        var sut = GetVhd1024False();
        sut.ReadBegin += (o, e) =>
        {
            eventRaised = true;
            eventE = e;
            eventO = o;
        };
        var address = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        var length = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        sut.Read(address, length);
        Assert.IsTrue(eventRaised);
        Assert.AreEqual(sut, eventO);
        Assert.IsNotNull(eventE);
        Assert.AreEqual(address, eventE.Address);
        Assert.AreEqual(length, eventE.Length);
    }

    [TestMethod]
    public void Read_RaisesEndEvent()
    {
        bool eventRaised = false;
        object? eventO = null;
        DiskOperationEventArgs? eventE = null;
        var sut = GetVhd1024False();
        sut.ReadEnd += (o, e) =>
        {
            eventRaised = true;
            eventE = e;
            eventO = o;
        };
        var address = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        var length = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        sut.Read(address, length);
        Assert.IsTrue(eventRaised);
        Assert.AreEqual(sut, eventO);
        Assert.IsNotNull(eventE);
        Assert.AreEqual(address, eventE.Address);
        Assert.AreEqual(length, eventE.Length);
    }

    [TestMethod]
    public void Read_DoesNotDelayOnFalse()
    {
        var sut = new VirtualHardDisk([], 1024*1024, false);
        var address = 0u;
        var length = sut.SizeBytes;
        var timer = Stopwatch.StartNew();
        sut.Read(address, length);
        timer.Stop();
        Assert.IsLessThan(100, timer.ElapsedMilliseconds);
    }

    [TestMethod]
    public void Read_DoesDelayOnTrue()
    {
        var sut = new VirtualHardDisk([], 1024*1024, true);
        var address = 0u;
        var length = sut.SizeBytes;
        var timer = Stopwatch.StartNew();
        sut.Read(address, length);
        timer.Stop();
        Assert.IsGreaterThan(100, timer.ElapsedMilliseconds);
        Assert.IsLessThan(1500, timer.ElapsedMilliseconds);
    }

    [TestMethod]
    public void Read_ThrowsOnLength0()
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.ReadEnd += (o, e) => eventFired = true;
        Assert.ThrowsExactly<ArgumentException>(() => sut.Read(0, 0));
        Assert.IsFalse(eventFired);
    }

    [TestMethod]
    [DataRow(1024u, 1u)]
    [DataRow(0u, 1025u)]
    [DataRow(512u, 513u)]
    public void Read_ThrowsOnOutOfBoudsAccess(ulong address, ulong length)
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.ReadEnd += (o, e) => eventFired = true;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sut.Read(address, length));
        Assert.IsFalse(eventFired);
    }


    [TestMethod]
    public void Read_ThrowsOnAddressMisaligned()
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.ReadEnd += (o, e) => eventFired = true;
        Assert.ThrowsExactly<ArgumentException>(() => sut.Read(1, 0));
        Assert.IsFalse(eventFired);
    }

    [TestMethod]
    [DataRow(0u, 1u)]
    [DataRow(0u, 1024u)]
    [DataRow(512u, 1u)]
    [DataRow(512u, 512u)]
    [DataRow(896u, 1u)]
    [DataRow(1024u-SectorLength, SectorLength)]
    public void Read_PassesOnValidData(ulong address, ulong length)
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.ReadEnd += (o, e) => eventFired = true;
        sut.Read(address, length);
        Assert.IsTrue(eventFired);
    }

    //Write
    //+write start event
    //+delay conditional
    //+write end event on success
    //+constraints
    // +Length 0 --> Argument Exc
    // +out of bounds by address or address + length --> Arg out of Range
    // +address misaligned
    // +no write end event on failure
    //+valid bounds tests
    //+we actually write data with valid boundaries
    //+write data does not wipe old data outside of our block
    //+write data can overwrite data
    //+throws on Bad or Locked sector
    [TestMethod]
    public void Write_RaisesBeginEvent()
    {
        bool eventRaised = false;
        object? eventO = null;
        DiskOperationEventArgs? eventE = null;
        var sut = GetVhd1024False();
        sut.WriteBegin += (o, e) =>
        {
            eventRaised = true;
            eventE = e;
            eventO = o;
        };
        var address = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        var length = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        sut.Write(address, length);
        Assert.IsTrue(eventRaised);
        Assert.AreEqual(sut, eventO);
        Assert.IsNotNull(eventE);
        Assert.AreEqual(address, eventE.Address);
        Assert.AreEqual(length, eventE.Length);
    }

    [TestMethod]
    public void Write_RaisesEndEvent()
    {
        bool eventRaised = false;
        object? eventO = null;
        DiskOperationEventArgs? eventE = null;
        var sut = GetVhd1024False();
        sut.WriteEnd += (o, e) =>
        {
            eventRaised = true;
            eventE = e;
            eventO = o;
        };
        var address = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        var length = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        sut.Write(address, length);
        Assert.IsTrue(eventRaised);
        Assert.AreEqual(sut, eventO);
        Assert.IsNotNull(eventE);
        Assert.AreEqual(address, eventE.Address);
        Assert.AreEqual(length, eventE.Length);
    }

    [TestMethod]
    public void Write_DoesNotDelayOnFalse()
    {
        var sut = new VirtualHardDisk([], 1024 * 1024, false);
        var address = 0u;
        var length = sut.SizeBytes;
        var timer = Stopwatch.StartNew();
        sut.Write(address, length);
        timer.Stop();
        Assert.IsLessThan(100, timer.ElapsedMilliseconds);
    }

    [TestMethod]
    public void Write_DoesDelayOnTrue()
    {
        var sut = new VirtualHardDisk([], 1024 * 1024, true);
        var address = 0u;
        var length = sut.SizeBytes;
        var timer = Stopwatch.StartNew();
        sut.Write(address, length);
        timer.Stop();
        Assert.IsGreaterThan(100, timer.ElapsedMilliseconds);
        Assert.IsLessThan(2000, timer.ElapsedMilliseconds);
    }

    [TestMethod]
    public void Write_ThrowsOnLength0()
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.WriteEnd += (o, e) => eventFired = true;
        Assert.ThrowsExactly<ArgumentException>(() => sut.Write(0, 0));
        Assert.IsFalse(eventFired);
    }

    [TestMethod]
    [DataRow(1024u, 1u)]
    [DataRow(0u, 1025u)]
    [DataRow(512u, 513u)]
    public void Write_ThrowsOnOutOfBoudsAccess(ulong address, ulong length)
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.WriteEnd += (o, e) => eventFired = true;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sut.Write(address, length));
        Assert.IsFalse(eventFired);
    }


    [TestMethod]
    public void Write_ThrowsOnAddressMisaligned()
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.WriteEnd += (o, e) => eventFired = true;
        Assert.ThrowsExactly<ArgumentException>(() => sut.Write(1, 1));
        Assert.IsFalse(eventFired);
    }

    [TestMethod]
    [DataRow(0u, 1u)]
    [DataRow(0u, 1024u)]
    [DataRow(512u, 1u)]
    [DataRow(512u, 512u)]
    [DataRow(896u, 1u)]
    [DataRow(1024u - SectorLength, SectorLength)]
    public void Write_PassesOnValidData(ulong address, ulong length)
    {
        Dictionary<ulong, SectorState> data = [];
        var sut = GetVhd1024False(data);
        bool eventFired = false;
        sut.WriteEnd += (o, e) => eventFired = true;
        sut.Write(address, length);
        Assert.IsTrue(eventFired);
        for (ulong i = 0; i < 1024 / SectorLength; i++)
        {
            var s = SectorState.Empty;
            var result = data.TryGetValue(i, out s);
            var iStart = address / SectorLength;
            var (iLength, iLengthRem) = Math.DivRem(length, SectorLength);
            if (iLengthRem > 0) iLength++;
            if(i >= iStart && i < iStart + iLength)
            {
                Assert.IsTrue(result);
                Assert.AreEqual(SectorState.Data, s);
            }
            else
            {
                Assert.IsFalse(result);
                Assert.AreEqual(SectorState.Empty, s);
            }
        }
    }

    [TestMethod]
    [DataRow(0u, 1u)]
    [DataRow(0u, 4u)]
    [DataRow(0u, 7u)]
    [DataRow(4u, 0u)]
    [DataRow(4u, 3u)]
    [DataRow(4u, 5u)]
    [DataRow(4u, 7u)]
    [DataRow(7u, 0u)]
    [DataRow(7u, 4u)]
    [DataRow(7u, 6u)]
    public void Write_DoesNotAffectExistingData(uint existingDataSector, uint writeDataSector)
    {
        Dictionary<ulong, SectorState> data = new() { {existingDataSector, SectorState.Data  } };
        var sut = GetVhd1024False(data);
        sut.Write(writeDataSector * SectorLength, 1);
        Assert.AreEqual(SectorState.Data, data[existingDataSector]);
        Assert.AreEqual(SectorState.Data, data[writeDataSector]);
        Assert.HasCount(2, data);
    }

    [TestMethod]
    [DataRow(0u, 0u)]
    [DataRow(4u, 3u)]
    [DataRow(7u, 5u)]
    public void Write_CanOverwriteExistingData(uint existingDataSector, uint writeDataSector)
    {
        Dictionary<ulong, SectorState> data = new() { { existingDataSector, SectorState.Data } };
        var sut = GetVhd1024False(data);
        sut.Write(writeDataSector * SectorLength, SectorLength * 3);
        Assert.HasCount(3, data);
        Assert.AreEqual(SectorState.Data, data[writeDataSector]);
        Assert.AreEqual(SectorState.Data, data[writeDataSector + 1]);
        Assert.AreEqual(SectorState.Data, data[writeDataSector + 2]);
    }

    [TestMethod]
    [DataRow(0u, SectorState.Bad ,0u)]
    [DataRow(4u, SectorState.Bad ,3u)]
    [DataRow(7u, SectorState.Bad ,5u)]
    [DataRow(0u, SectorState.Locked ,0u)]
    [DataRow(4u, SectorState.Locked ,3u)]
    [DataRow(7u, SectorState.Locked ,5u)]
    public void Write_ThrowsOnBadOrLockedExistingData(uint existingDataSector, SectorState existingState, uint writeDataSector)
    {
        Dictionary<ulong, SectorState> data = new() { { existingDataSector, existingState } };
        var sut = GetVhd1024False(data);
        Assert.ThrowsExactly<InvalidOperationException>(() => sut.Write(writeDataSector * SectorLength, SectorLength * 3));
        Assert.HasCount((int)(1 + existingDataSector - writeDataSector), data);
        Assert.AreEqual(existingState, data[existingDataSector]);
        for (uint i = writeDataSector; i < existingDataSector; i++)
        {
            Assert.AreEqual(SectorState.Data, data[i]);
        }
    }

    //Clear
    //+write start event
    //+delay conditional
    //+write end event on success
    //+constraints
    // +Length 0 --> Argument Exc
    // +out of bounds by address or address + length --> Arg out of Range
    // +address misaligned
    // +no write end event on failure
    //+valid bounds tests
    //+we can "clear" Empty sectors
    //+throws on Bad or Locked sector
    [TestMethod]
    public void Clear_RaisesBeginEvent()
    {
        bool eventRaised = false;
        object? eventO = null;
        DiskOperationEventArgs? eventE = null;
        var sut = GetVhd1024False();
        sut.WriteBegin += (o, e) =>
        {
            eventRaised = true;
            eventE = e;
            eventO = o;
        };
        var address = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        var length = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        sut.Clear(address, length);
        Assert.IsTrue(eventRaised);
        Assert.AreEqual(sut, eventO);
        Assert.IsNotNull(eventE);
        Assert.AreEqual(address, eventE.Address);
        Assert.AreEqual(length, eventE.Length);
    }

    [TestMethod]
    public void Clear_RaisesEndEvent()
    {
        bool eventRaised = false;
        object? eventO = null;
        DiskOperationEventArgs? eventE = null;
        var sut = GetVhd1024False();
        sut.WriteEnd += (o, e) =>
        {
            eventRaised = true;
            eventE = e;
            eventO = o;
        };
        var address = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        var length = (ulong)Random.Shared.Next(1, 4) * SectorLength;
        sut.Clear(address, length);
        Assert.IsTrue(eventRaised);
        Assert.AreEqual(sut, eventO);
        Assert.IsNotNull(eventE);
        Assert.AreEqual(address, eventE.Address);
        Assert.AreEqual(length, eventE.Length);
    }

    [TestMethod]
    public void Clear_DoesNotDelayOnFalse()
    {
        var sut = new VirtualHardDisk([], 1024 * 1024, false);
        var address = 0u;
        var length = sut.SizeBytes;
        var timer = Stopwatch.StartNew();
        sut.Clear(address, length);
        timer.Stop();
        Assert.IsLessThan(100, timer.ElapsedMilliseconds);
    }

    [TestMethod]
    public void Clear_DoesDelayOnTrue()
    {
        var sut = new VirtualHardDisk([], 1024 * 1024, true);
        var address = 0u;
        var length = sut.SizeBytes;
        var timer = Stopwatch.StartNew();
        sut.Clear(address, length);
        timer.Stop();
        Assert.IsGreaterThan(100, timer.ElapsedMilliseconds);
        Assert.IsLessThan(2000, timer.ElapsedMilliseconds);
    }

    [TestMethod]
    public void Clear_ThrowsOnLength0()
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.WriteEnd += (o, e) => eventFired = true;
        Assert.ThrowsExactly<ArgumentException>(() => sut.Clear(0, 0));
        Assert.IsFalse(eventFired);
    }

    [TestMethod]
    [DataRow(1024u, 1u)]
    [DataRow(0u, 1025u)]
    [DataRow(512u, 513u)]
    public void Clear_ThrowsOnOutOfBoudsAccess(ulong address, ulong length)
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.WriteEnd += (o, e) => eventFired = true;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sut.Clear(address, length));
        Assert.IsFalse(eventFired);
    }


    [TestMethod]
    public void Clear_ThrowsOnAddressMisaligned()
    {
        var sut = GetVhd1024False();
        bool eventFired = false;
        sut.WriteEnd += (o, e) => eventFired = true;
        Assert.ThrowsExactly<ArgumentException>(() => sut.Clear(1, 1));
        Assert.IsFalse(eventFired);
    }

    [TestMethod]
    [DataRow(0u, 1u)]
    [DataRow(0u, 1024u)]
    [DataRow(512u, 1u)]
    [DataRow(512u, 512u)]
    [DataRow(896u, 1u)]
    [DataRow(1024u - SectorLength, SectorLength)]
    public void Clear_PassesOnValidData(ulong address, ulong length)
    {
        Dictionary<ulong, SectorState> data = new() {
            {0, SectorState.Data },
            {1, SectorState.Data },
            {2, SectorState.Data },
            {3, SectorState.Data },
            {4, SectorState.Data },
            {5, SectorState.Data },
            {6, SectorState.Data },
            {7, SectorState.Data },
        };
        var sut = GetVhd1024False(data);
        bool eventFired = false;
        sut.WriteEnd += (o, e) => eventFired = true;
        sut.Clear(address, length);
        Assert.IsTrue(eventFired);
        for (ulong i = 0; i < 1024 / SectorLength; i++)
        {
            var sectorClear = !data.ContainsKey(i);
            var iStart = address / SectorLength;
            var (iLength, iLengthRem) = Math.DivRem(length, SectorLength);
            if (iLengthRem > 0) iLength++;
            Assert.AreEqual(i >= iStart && i < iStart + iLength, sectorClear);
        }
    }
    [TestMethod]
    public void Clear_PassesOnSetEmptySector()
    {
        Dictionary<ulong, SectorState> data = new() { { 0, SectorState.Empty } };
        var sut = GetVhd1024False(data);
        bool eventFired = false;
        sut.WriteEnd += (o, e) => eventFired = true;
        sut.Clear(0, SectorLength);
        Assert.IsTrue(eventFired);
        Assert.HasCount(0, data);
    }

    [TestMethod]
    [DataRow(0u, SectorState.Bad, 0u)]
    [DataRow(4u, SectorState.Bad, 3u)]
    [DataRow(7u, SectorState.Bad, 5u)]
    [DataRow(0u, SectorState.Locked, 0u)]
    [DataRow(4u, SectorState.Locked, 3u)]
    [DataRow(7u, SectorState.Locked, 5u)]
    public void Clear_ThrowsOnBadOrLockedExistingData(uint existingDataSector, SectorState existingState, uint clearDataSector)
    {
        Dictionary<ulong, SectorState> data = new() { { existingDataSector, existingState } };
        var sut = GetVhd1024False(data);
        Assert.ThrowsExactly<InvalidOperationException>(() => sut.Clear(clearDataSector * SectorLength, SectorLength * 3));
        Assert.HasCount(1, data);
    }
}
