using System.Windows;
using MTool.App.ViewModels;

namespace MTool.App.Services;

/// <summary>The Windows clipboard; throws while another program holds it open.</summary>
internal sealed class WpfClipboard : IClipboard
{
    public void SetText(string text) => Clipboard.SetText(text);
}
