using MTool.Core.Ec;

namespace MTool.Core.Profiles;

/// <summary>Whole-curve rules every fan table must pass before it can reach the EC (plan.md §4.1).</summary>
public static class CurveValidator
{
    public const int PointCount = 7;

    /// <summary>The last step must start at or below this temperature...</summary>
    public const int SafetyFloorMaxLastThresholdC = 90;

    /// <summary>...and run at least this fast.</summary>
    public const int SafetyFloorMinLastSpeedPercent = 80;

    /// <summary>The fan must leave its idle speed by this temperature...</summary>
    public const int MaxFirstStepThresholdC = 65;

    /// <summary>...and run at least <see cref="EnvelopeMinSpeedPercent"/> once the chip reaches this temperature.</summary>
    public const int EnvelopeTemperatureC = 75;

    public const int EnvelopeMinSpeedPercent = 50;

    /// <summary>Returns human-readable (Turkish) problems; empty when the curve is valid.</summary>
    public static IReadOnlyList<string> Validate(FanCurve curve)
    {
        var points = curve.Points;
        if (points.Count != PointCount)
        {
            return [$"Eğri {PointCount} noktadan oluşmalı ({points.Count} var)."];
        }

        var errors = new List<string>();
        if (points[0].UpThresholdC != 0 || points[0].DownThresholdC != 0)
        {
            errors.Add("İlk nokta boştaki hızdır, eşik taşımaz.");
        }

        for (var i = 0; i < PointCount; i++)
        {
            CheckSpeed(points, i, errors);
            if (i > 0)
            {
                CheckThresholds(points, i, errors);
            }
        }

        CheckSafetyFloor(points[^1], errors);
        CheckEnvelope(points, errors);
        return errors.AsReadOnly();
    }

    /// <summary>Stops curves that keep the fan idle or slow until the last step (e.g. 0 % up to 84 °C).</summary>
    private static void CheckEnvelope(IReadOnlyList<FanPoint> points, List<string> errors)
    {
        if (points[1].UpThresholdC > MaxFirstStepThresholdC)
        {
            errors.Add($"Güvenlik zarfı: ilk kademe en geç {MaxFirstStepThresholdC} °C'de başlamalı.");
        }

        var speedAtEnvelope = points.Last(p => p.UpThresholdC <= EnvelopeTemperatureC).SpeedPercent;
        if (speedAtEnvelope < EnvelopeMinSpeedPercent)
        {
            errors.Add($"Güvenlik zarfı: {EnvelopeTemperatureC} °C'de fan en az %{EnvelopeMinSpeedPercent} dönmeli (şu an %{speedAtEnvelope}).");
        }
    }

    private static void CheckSpeed(IReadOnlyList<FanPoint> points, int i, List<string> errors)
    {
        var speed = points[i].SpeedPercent;
        if (speed is < 0 or > EcWriteRules.MaxSpeedPercent)
        {
            errors.Add($"Adım {i}: fan hızı %0-{EcWriteRules.MaxSpeedPercent} aralığında olmalı.");
        }

        if (i > 0 && speed < points[i - 1].SpeedPercent)
        {
            errors.Add($"Adım {i}: fan hızı bir önceki adımdan düşük olamaz.");
        }
    }

    private static void CheckThresholds(IReadOnlyList<FanPoint> points, int i, List<string> errors)
    {
        var (up, down) = (points[i].UpThresholdC, points[i].DownThresholdC);
        var previousUp = points[i - 1].UpThresholdC;

        if (up is < EcWriteRules.MinUpThresholdC or > EcWriteRules.MaxUpThresholdC)
        {
            errors.Add($"Adım {i}: yukarı eşik {EcWriteRules.MinUpThresholdC}-{EcWriteRules.MaxUpThresholdC} °C olmalı.");
        }

        if (i > 1 && up <= previousUp)
        {
            errors.Add($"Adım {i}: yukarı eşikler artan olmalı.");
        }

        if (down >= up || down <= previousUp)
        {
            errors.Add($"Adım {i}: aşağı eşik, önceki adımın yukarı eşiğinden ({previousUp} °C) büyük ve bu adımınkinden ({up} °C) küçük olmalı.");
        }

        if (up - down > EcWriteRules.MaxDownOffsetC)
        {
            errors.Add($"Adım {i}: aşağı eşik, yukarı eşikten en fazla {EcWriteRules.MaxDownOffsetC} °C düşük olabilir.");
        }
    }

    private static void CheckSafetyFloor(FanPoint last, List<string> errors)
    {
        if (last.UpThresholdC > SafetyFloorMaxLastThresholdC)
        {
            errors.Add($"Güvenlik tabanı: son kademe en geç {SafetyFloorMaxLastThresholdC} °C'de başlamalı.");
        }

        if (last.SpeedPercent < SafetyFloorMinLastSpeedPercent)
        {
            errors.Add($"Güvenlik tabanı: son kademe en az %{SafetyFloorMinLastSpeedPercent} olmalı.");
        }
    }
}
