using MTool.App.ViewModels;

namespace MTool.Tests.Fakes;

/// <summary>Keeps what was copied; can fail the way a clipboard held by another program does.</summary>
internal sealed class FakeClipboard : IClipboard
{
    public List<string> Texts { get; } = [];

    public bool Fails { get; set; }

    public void SetText(string text)
    {
        if (Fails)
        {
            throw new System.Runtime.InteropServices.COMException("OpenClipboard failed", unchecked((int)0x800401D0));
        }

        Texts.Add(text);
    }
}
