namespace MTool.Tests.Device.Config;

/// <summary>
/// The P65 Creator 9SE (16Q4EMS2.107) values as literal tables, written by hand from the code at
/// 0.9.0 and never read from the production statics. They pin today's behaviour while the maps move
/// into device configs: the golden tests check the statics against them, the equivalence tests check
/// the embedded config. Tuples instead of production types, so the tables outlive a type rename.
/// </summary>
internal static class P65Golden
{
    public const string Firmware = "16Q4EMS2.107";
    public const byte FirmwareVersion = 0xA0;
    public const int FirmwareVersionLength = 12;
    public const byte FirmwareDate = 0xAC;
    public const int FirmwareDateLength = 8;

    public static readonly (byte Temperature, byte SpeedPercent, byte RpmHigh, byte UpThresholdsStart, byte SpeedsStart)
        CpuFan = (0x68, 0x71, 0xCC, 0x6A, 0x72);

    public static readonly (byte Temperature, byte SpeedPercent, byte RpmHigh, byte UpThresholdsStart, byte SpeedsStart)
        GpuFan = (0x80, 0x89, 0xCA, 0x82, 0x8A);

    public const byte CoolerBoost = 0x98;
    public const byte ChargeLimit = 0xEF;
    public const byte PerformanceMode = 0xF2;
    public const byte FanMode = 0xF4;

    public static readonly byte[] CpuUpThresholds = [0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F];
    public static readonly byte[] CpuSpeeds = [0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78];
    public static readonly byte[] GpuUpThresholds = [0x82, 0x83, 0x84, 0x85, 0x86, 0x87];
    public static readonly byte[] GpuSpeeds = [0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F, 0x90];

    /// <summary>26 table registers in register order.</summary>
    public static readonly byte[] FanTableRegisters =
    [
        0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
        0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
        0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F, 0x90,
    ];

    /// <summary>The 30 registers the gateway may write, in register order.</summary>
    public static readonly byte[] WritableRegisters =
    [
        0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
        0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
        0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F, 0x90,
        0x98, 0xEF, 0xF2, 0xF4,
    ];

    /// <summary>What <c>PortWriteGuard</c> reads around a port plan: both tables, performance and fan mode.</summary>
    public static readonly byte[] PortGuardWatched =
    [
        0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
        0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78,
        0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x8A, 0x8B, 0x8C, 0x8D, 0x8E, 0x8F, 0x90,
        0xF2, 0xF4,
    ];

    public static readonly byte[] PortRegisters = [0x98, 0xEF];

    /// <summary>Gateway write order: speeds 0, up thresholds 1, other settings 2, fan mode 3.</summary>
    public static readonly (byte Register, int Order)[] WriteOrder =
    [
        (0x72, 0), (0x78, 0), (0x8A, 0), (0x90, 0),
        (0x6A, 1), (0x6F, 1), (0x82, 1), (0x87, 1),
        (0x98, 2), (0xEF, 2), (0xF2, 2),
        (0xF4, 3),
    ];

    public static readonly IReadOnlyDictionary<byte, (string Class, int Index)> WmiFields =
        new Dictionary<byte, (string Class, int Index)>
        {
            // Firmware version (12) and date (8): MSI_Software[6..25].
            [0xA0] = ("MSI_Software", 6),
            [0xA1] = ("MSI_Software", 7),
            [0xA2] = ("MSI_Software", 8),
            [0xA3] = ("MSI_Software", 9),
            [0xA4] = ("MSI_Software", 10),
            [0xA5] = ("MSI_Software", 11),
            [0xA6] = ("MSI_Software", 12),
            [0xA7] = ("MSI_Software", 13),
            [0xA8] = ("MSI_Software", 14),
            [0xA9] = ("MSI_Software", 15),
            [0xAA] = ("MSI_Software", 16),
            [0xAB] = ("MSI_Software", 17),
            [0xAC] = ("MSI_Software", 18),
            [0xAD] = ("MSI_Software", 19),
            [0xAE] = ("MSI_Software", 20),
            [0xAF] = ("MSI_Software", 21),
            [0xB0] = ("MSI_Software", 22),
            [0xB1] = ("MSI_Software", 23),
            [0xB2] = ("MSI_Software", 24),
            [0xB3] = ("MSI_Software", 25),

            // CPU fan: temperature, speed %, up thresholds [5..10], speeds [11..17].
            [0x68] = ("MSI_CPU", 1),
            [0x71] = ("MSI_CPU", 2),
            [0x6A] = ("MSI_CPU", 5),
            [0x6B] = ("MSI_CPU", 6),
            [0x6C] = ("MSI_CPU", 7),
            [0x6D] = ("MSI_CPU", 8),
            [0x6E] = ("MSI_CPU", 9),
            [0x6F] = ("MSI_CPU", 10),
            [0x72] = ("MSI_CPU", 11),
            [0x73] = ("MSI_CPU", 12),
            [0x74] = ("MSI_CPU", 13),
            [0x75] = ("MSI_CPU", 14),
            [0x76] = ("MSI_CPU", 15),
            [0x77] = ("MSI_CPU", 16),
            [0x78] = ("MSI_CPU", 17),

            // GPU fan: same layout in MSI_VGA.
            [0x80] = ("MSI_VGA", 1),
            [0x89] = ("MSI_VGA", 2),
            [0x82] = ("MSI_VGA", 5),
            [0x83] = ("MSI_VGA", 6),
            [0x84] = ("MSI_VGA", 7),
            [0x85] = ("MSI_VGA", 8),
            [0x86] = ("MSI_VGA", 9),
            [0x87] = ("MSI_VGA", 10),
            [0x8A] = ("MSI_VGA", 11),
            [0x8B] = ("MSI_VGA", 12),
            [0x8C] = ("MSI_VGA", 13),
            [0x8D] = ("MSI_VGA", 14),
            [0x8E] = ("MSI_VGA", 15),
            [0x8F] = ("MSI_VGA", 16),
            [0x90] = ("MSI_VGA", 17),

            // RPM bytes run backwards.
            [0xCD] = ("MSI_AP", 2),
            [0xCC] = ("MSI_AP", 3),
            [0xCB] = ("MSI_AP", 4),
            [0xCA] = ("MSI_AP", 5),

            [0xF2] = ("MSI_System", 7),
            [0xF4] = ("MSI_System", 9),
        };

    public static readonly (int Up, int Speed)[] FactoryCpuCurve = [(0, 45), (55, 50), (64, 60), (70, 70), (76, 75), (82, 80), (88, 80)];
    public static readonly (int Up, int Speed)[] FactoryGpuCurve = [(0, 0), (55, 50), (61, 60), (65, 70), (71, 80), (77, 90), (86, 90)];
    public static readonly int[] CpuDownOffsets = [8, 3, 3, 3, 3, 3];
    public static readonly int[] GpuDownOffsets = [8, 3, 3, 3, 3, 5];

    public static readonly (int Up, int Speed)[] CoolCpuCurve = [(0, 50), (50, 60), (58, 70), (65, 80), (72, 90), (78, 100), (85, 100)];
    public static readonly (int Up, int Speed)[] CoolGpuCurve = [(0, 40), (50, 55), (57, 65), (63, 75), (69, 85), (75, 100), (82, 100)];
    public static readonly (int Up, int Speed)[] SilentCpuCurve = [(0, 35), (60, 45), (68, 55), (75, 65), (80, 75), (85, 85), (90, 100)];
    public static readonly (int Up, int Speed)[] SilentGpuCurve = [(0, 0), (60, 40), (67, 50), (73, 60), (78, 70), (83, 85), (89, 100)];

    /// <summary>Fan curve plans in the order the planner emits them: CPU up, CPU speeds, GPU up, GPU speeds.</summary>
    public static readonly (byte Register, byte Value)[] DefaultPlan =
    [
        (0x6A, 55), (0x6B, 64), (0x6C, 70), (0x6D, 76), (0x6E, 82), (0x6F, 88),
        (0x72, 45), (0x73, 50), (0x74, 60), (0x75, 70), (0x76, 75), (0x77, 80), (0x78, 80),
        (0x82, 55), (0x83, 61), (0x84, 65), (0x85, 71), (0x86, 77), (0x87, 86),
        (0x8A, 0), (0x8B, 50), (0x8C, 60), (0x8D, 70), (0x8E, 80), (0x8F, 90), (0x90, 90),
    ];

    public static readonly (byte Register, byte Value)[] CoolPlan =
    [
        (0x6A, 50), (0x6B, 58), (0x6C, 65), (0x6D, 72), (0x6E, 78), (0x6F, 85),
        (0x72, 50), (0x73, 60), (0x74, 70), (0x75, 80), (0x76, 90), (0x77, 100), (0x78, 100),
        (0x82, 50), (0x83, 57), (0x84, 63), (0x85, 69), (0x86, 75), (0x87, 82),
        (0x8A, 40), (0x8B, 55), (0x8C, 65), (0x8D, 75), (0x8E, 85), (0x8F, 100), (0x90, 100),
    ];

    public static readonly (byte Register, byte Value)[] SilentPlan =
    [
        (0x6A, 60), (0x6B, 68), (0x6C, 75), (0x6D, 80), (0x6E, 85), (0x6F, 90),
        (0x72, 35), (0x73, 45), (0x74, 55), (0x75, 65), (0x76, 75), (0x77, 85), (0x78, 100),
        (0x82, 60), (0x83, 67), (0x84, 73), (0x85, 78), (0x86, 83), (0x87, 89),
        (0x8A, 0), (0x8B, 40), (0x8C, 50), (0x8D, 60), (0x8E, 70), (0x8F, 85), (0x90, 100),
    ];

    public static readonly (string Mode, byte Value)[] PerformanceModes = [("High", 0xC0), ("Balanced", 0xC1), ("Eco", 0xC2)];
    public static readonly (string Mode, byte Value)[] FanModes = [("Auto", 0x0D), ("Advanced", 0x8D)];

    public const int MinChargeLimitPercent = 50;
    public const int MaxChargeLimitPercent = 100;

    /// <summary>
    /// <c>CheckStatic</c> boundaries: register, value, accepted. Fan table limits 30-95 °C and 100 %,
    /// charge limit 0x80 | 50-100, exact mode values, nothing outside the whitelist.
    /// </summary>
    public static readonly (byte Register, byte Value, bool Accepted)[] StaticRules =
    [
        (0x6A, 29, false), (0x6A, 30, true), (0x6A, 95, true), (0x6A, 96, false),
        (0x87, 29, false), (0x87, 30, true), (0x87, 95, true), (0x87, 96, false),
        (0x72, 0, true), (0x72, 100, true), (0x72, 101, false),
        (0x90, 0, true), (0x90, 100, true), (0x90, 101, false),
        (0xEF, 0xB2, true), (0xEF, 0xE4, true), (0xEF, 0xD0, true),
        (0xEF, 0xB1, false), (0xEF, 0xE5, false), (0xEF, 0x31, false), (0xEF, 0x50, false),
        (0xF2, 0xC0, true), (0xF2, 0xC1, true), (0xF2, 0xC2, true),
        (0xF2, 0xC4, false), (0xF2, 0x80, false),
        (0xF4, 0x0D, true), (0xF4, 0x8D, true),
        (0xF4, 0x4D, false), (0xF4, 0x0C, false),
        (0x98, 0x02, true), (0x98, 0x82, true),
        (0x7A, 8, false), (0x92, 8, false), (0xA0, 0x31, false), (0x68, 50, false), (0xCC, 0, false),
    ];
}
