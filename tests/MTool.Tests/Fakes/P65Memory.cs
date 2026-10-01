namespace MTool.Tests.Fakes;

/// <summary>EC contents read from the real P65 (factory Default fan tables).</summary>
internal static class P65Memory
{
    public static FakeEcRegisters FactorySnapshot()
    {
        var ec = new FakeEcRegisters();
        ec.LoadAscii(0xA0, "16Q4EMS2.107");
        ec.LoadAscii(0xAC, "05132019");
        ec.Load(0x68, 60);
        ec.Load(0x71, 50);
        ec.Load(0x80, 46);
        ec.Load(0x89, 0);
        ec.Load(0xCA, 0, 0);
        ec.Load(0xCC, 0, 157);
        ec.Load(0x6A, 55, 64, 70, 76, 82, 88);
        ec.Load(0x72, 45, 50, 60, 70, 75, 80, 80);
        ec.Load(0x7A, 8, 3, 3, 3, 3, 3);
        ec.Load(0x82, 55, 61, 65, 71, 77, 86);
        ec.Load(0x8A, 0, 50, 60, 70, 80, 90, 90);
        ec.Load(0x92, 8, 3, 3, 3, 3, 5);
        ec.Load(0x98, 0x02);
        ec.Load(0xEF, 0xD0);
        ec.Load(0xF2, 0xC0);
        ec.Load(0xF3, 0x81);
        ec.Load(0xF4, 0x8D);
        return ec;
    }
}
