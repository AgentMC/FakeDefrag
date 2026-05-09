namespace DefragCore.UnitTests
{
    [TestClass]
    public sealed class VirtualUnitSequenceTests
    {
        [TestMethod]
        [DataRow(0ul, 0u)]
        [DataRow(ulong.MaxValue, uint.MaxValue)]
        public void ConstructorTest(ulong address, uint number)
        {
            var sut = new VirtualUnitSequence(address, number);
            Assert.AreEqual(address, sut.Address);
            Assert.AreEqual(number, sut.NumUnits);
        }
    }
}
