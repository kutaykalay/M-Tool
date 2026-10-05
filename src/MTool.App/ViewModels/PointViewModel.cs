using MTool.Core.Profiles;

namespace MTool.App.ViewModels;

public enum CurveFan
{
    Cpu,
    Gpu,
}

/// <param name="IsBuiltIn">Ships with M-Tool: read-only in the editor, but it can be copied and applied.</param>
public sealed record ProfileItem(string Name, bool IsBuiltIn);

/// <summary>One point of the fan shown in the editor; <see cref="Limits"/> is where it may be moved.</summary>
public sealed record PointViewModel(int Index, int UpC, int SpeedPercent, PointLimits Limits)
{
    public bool IsIdle => Index == 0;
}
