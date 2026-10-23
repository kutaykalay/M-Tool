using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MTool.Core.Device.Config;

namespace MTool.Core.Diagnostics;

/// <summary>
/// What "Send EC data" sends: the report's read-only data as JSON (schema version 2), written once so
/// the preview and the request body are the same bytes. The server checks the same schema
/// (<c>tests/MTool.Tests/Contracts/ec-submission-v2.sample.json</c> is the contract).
/// What it leaves out on purpose: the DSDT, the time, the language, any error message or path, and
/// every name of the user or the computer. A part that could not be read is a fixed code, never text.
/// </summary>
public static class EcSubmission
{
    public const int SchemaVersion = 2;

    /// <summary>The server refuses larger bodies; M-Tool does not send them.</summary>
    public const int MaxBytes = 32 * 1024;

    /// <summary>Longest text value, in UTF-16 units; the server refuses longer ones.</summary>
    public const int MaxTextLength = 128;

    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        NewLine = "\n",
        // Shown to the user before it is sent and never placed in HTML: "<hidden>" and "İ" stay readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="privateWords">The user and machine names: masked in every value, see <see cref="PrivateText"/>.</param>
    public static string ToJson(DeviceReportInput input, IReadOnlyCollection<string> privateWords)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer, Options))
        {
            var text = new Cleaner(privateWords);
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", SchemaVersion);
            json.WriteStartObject("app");
            json.WriteString("version", text.Clean(input.AppVersion));
            json.WriteEndObject();
            WriteSystem(json, input.Computer.Value, text);
            WriteWmi(json, input, text);
            WriteMsi(json, input.Msi);
            WriteEc(json, input, text);
            WriteWmi2(json, input.Wmi2, text);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteSystem(Utf8JsonWriter json, SystemInfo? system, Cleaner text)
    {
        json.WriteStartObject("system");
        json.WriteString("manufacturer", text.Clean(system?.Manufacturer));
        json.WriteString("model", text.Clean(system?.Model));
        json.WriteString("baseBoard", text.Clean(system?.BaseBoard));
        json.WriteString("biosVersion", text.Clean(system?.BiosVersion));
        json.WriteString("biosDate", text.Clean(system?.BiosDate));
        json.WriteString("cpu", text.Clean(system?.Cpu));
        json.WriteString("windows", text.Clean(system?.Windows));
        json.WriteEndObject();
    }

    private static void WriteWmi(Utf8JsonWriter json, DeviceReportInput input, Cleaner text)
    {
        json.WriteStartObject("wmi");
        json.WriteString("interface", InterfaceName(input.Interface));
        json.WriteString("interfaceStatus", DiagnosticWords.Status(input.Interface.Status));
        WriteFailure(json, input.Interface.Failure);
        json.WriteString("classesStatus", input.Classes.IsOk ? "ok" : "failed");
        json.WriteStartArray("classes");
        foreach (var info in input.Classes.Value ?? [])
        {
            json.WriteStartObject();
            json.WriteString("name", text.Clean(info.Name) ?? "");
            json.WriteStartArray("properties");
            foreach (var property in info.Properties)
            {
                json.WriteStringValue(text.Clean(property) ?? "");
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>The step and the number only: Windows' message never goes.</summary>
    private static void WriteFailure(Utf8JsonWriter json, WmiFailure? failure)
    {
        if (failure is null)
        {
            json.WriteNull("failure");
            return;
        }

        json.WriteStartObject("failure");
        json.WriteString("stage", DiagnosticWords.Stage(failure.Stage));
        json.WriteString("code", failure.TimedOut ? null : failure.CodeText);
        json.WriteEndObject();
    }

    private static void WriteMsi(Utf8JsonWriter json, MsiInfo msi)
    {
        json.WriteStartObject("msi");
        json.WriteStartObject("schema");
        json.WriteString("registration", DiagnosticWords.Registration(msi.Schema.MofImagePath));
        json.WriteString("file", DiagnosticWords.File(msi.Schema.File));
        json.WriteString("signature", DiagnosticWords.Signature(msi.Schema.Signature));
        json.WriteEndObject();
        json.WriteStartObject("software");
        WriteFlag(json, "installed", msi.Software.Installed);
        WriteFlag(json, "running", msi.Software.Running);
        json.WriteEndObject();
        json.WriteEndObject();
    }

    private static void WriteFlag(Utf8JsonWriter json, string name, bool? value)
    {
        if (value is { } flag)
        {
            json.WriteBoolean(name, flag);
        }
        else
        {
            json.WriteNull(name);
        }
    }

    private static void WriteEc(Utf8JsonWriter json, DeviceReportInput input, Cleaner text)
    {
        var ec = input.Ec.Value;
        json.WriteStartObject("ec");
        json.WriteString("status", ec is not null ? "ok" : input.Interface.Interface == WmiInterface.Wmi1 ? "failed" : "not_read");
        json.WriteString("firmware", text.Clean(ec?.Firmware?.Version));
        json.WriteString("firmwareDate", text.Clean(ec?.Firmware?.Date));
        json.WriteStartObject("match");
        json.WriteString("kind", (ec?.Match.Kind ?? MatchKind.None).ToString());
        json.WriteString("recordId", text.Clean(ec?.Match.RecordId));
        json.WriteEndObject();
        json.WriteString("layoutId", text.Clean(ec?.LayoutId));
        json.WriteStartArray("fields");
        foreach (var field in ec?.Fields.Readouts ?? [])
        {
            json.WriteStartObject();
            json.WriteString("register", Hex(field.Register));
            json.WriteString("field", text.Clean(field.Field) ?? "");
            json.WriteString("value", field.Value is { } value ? Hex(value) : null);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("skipped", ec?.Fields.Skipped ?? 0);
        json.WriteEndObject();
    }

    private static void WriteWmi2(Utf8JsonWriter json, Section<Wmi2Readout>? wmi2, Cleaner text)
    {
        json.WriteStartObject("wmi2");
        json.WriteString("status", wmi2 is null ? "not_asked" : wmi2.IsOk ? "ok" : "failed");
        json.WriteStartArray("calls");
        foreach (var call in wmi2?.Value?.Calls ?? [])
        {
            json.WriteStartObject();
            json.WriteString("method", text.Clean(call.Method) ?? "");
            json.WriteNumber("sub", call.Sub);
            json.WriteString("packet", call.Packet is { Count: Wmi2Probe.PacketLength } packet ? string.Join(' ', packet.Select(Hex2)) : null);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>"none" only when WMI answered without an MSI class; a probe that failed is "unknown".</summary>
    private static string InterfaceName(WmiProbeOutcome probe) => probe switch
    {
        { Failure: not null } => "unknown",
        { Interface: WmiInterface.Wmi1 } => "WMI1",
        { Interface: WmiInterface.Wmi2 } => "WMI2",
        _ => "none",
    };


    private static string Hex(byte value) => "0x" + Hex2(value);

    private static string Hex2(byte value) => value.ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>Masks the private words, drops what is not shown, makes white space one space, cuts to <see cref="MaxTextLength"/>.</summary>
    private sealed class Cleaner(IReadOnlyCollection<string> privateWords)
    {
        /// <returns>Null when nothing is left.</returns>
        public string? Clean(string? value)
        {
            if (value is null)
            {
                return null;
            }

            var visible = Visible(PrivateText.Mask(value, privateWords));
            if (visible.Length == 0)
            {
                return null;
            }

            // A look-alike form of a name (full-width letters) survives the mask: then nothing of the value goes.
            return PrivateText.HoldsPrivateName(visible, privateWords) ? PrivateText.Hidden : Cut(visible);
        }

        private static string Visible(string value)
        {
            var text = new StringBuilder();
            var lastWasSpace = true; // also trims the start
            foreach (var rune in value.EnumerateRunes())
            {
                if (rune == Rune.ReplacementChar || PrivateText.IsUnprintable(rune))
                {
                    continue;
                }

                var isSpace = Rune.IsWhiteSpace(rune);
                if (!(isSpace && lastWasSpace))
                {
                    text.Append(isSpace ? " " : rune.ToString());
                }

                lastWasSpace = isSpace;
            }

            return text.ToString().TrimEnd();
        }

        private static string Cut(string value)
        {
            if (value.Length <= MaxTextLength)
            {
                return value;
            }

            var length = 0;
            foreach (var rune in value.EnumerateRunes())
            {
                if (length + rune.Utf16SequenceLength > MaxTextLength)
                {
                    break;
                }

                length += rune.Utf16SequenceLength;
            }

            return value[..length].TrimEnd();
        }
    }
}
