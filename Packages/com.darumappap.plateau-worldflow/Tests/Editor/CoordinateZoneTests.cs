using NUnit.Framework;

namespace DarumaPPAP.PlateauWorldFlow.Import
{
    /// <summary>
    /// 告示で一意に解決できる自治体と、詳細な地域照合を要する場合を確認する。
    /// </summary>
    internal sealed class CoordinateZoneTests
    {
        [TestCase("13111", 9)]
        [TestCase("14201", 9)]
        [TestCase("27100", 6)]
        [TestCase("42201", 1)]
        [TestCase("01100", 0)]
        [TestCase("47201", 0)]
        [TestCase("13421", 0)]
        [TestCase("6668", 0)]
        public void ResolvesOnlyMunicipalitiesWithSufficientRegionEvidence(string code, int expectedZone)
        {
            Assert.That(new CoordinateZone().FromMunicipality(code), Is.EqualTo(expectedZone));
        }
    }
}
