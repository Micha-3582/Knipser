using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace Knipser;

/// <summary>Aktualisierung über GitHub Releases: neueste Version suchen, Knipser.exe laden, austauschen, neu starten.</summary>
public static class Updater
{
    const string Api = "https://api.github.com/repos/Micha-3582/Knipser/releases/latest";
    const string AssetName = "Knipser.exe";

    public record Info(Version Version, string Tag, string Url, string Notes);

    public static Version Current
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    public static string CurrentText => Current.ToString();

    static HttpClient NewClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Knipser-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    /// <summary>Liefert Infos zur neuen Version, oder null wenn aktuell. Wirft bei Netzwerkfehlern.</summary>
    public static async Task<Info?> CheckAsync()
    {
        using var http = NewClient();
        using var doc = JsonDocument.Parse(await http.GetStringAsync(Api));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var ver)) return null;
        if (ver <= Current) return null;

        string? url = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
            if (string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                url = a.GetProperty("browser_download_url").GetString();
        if (url == null) return null;

        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new Info(ver, tag, url, notes);
    }

    /// <summary>Neue Exe laden und einen Hilfsprozess starten, der sie nach dem Beenden austauscht und Knipser neu startet.</summary>
    public static async Task PrepareInstallAsync(Info info)
    {
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("Programmpfad unbekannt.");
        var tmp = Path.Combine(Path.GetTempPath(), "Knipser-update-" + info.Tag + ".exe");

        using (var http = NewClient())
        {
            http.Timeout = TimeSpan.FromMinutes(5);
            var bytes = await http.GetByteArrayAsync(info.Url);
            if (bytes.Length < 100_000) throw new InvalidOperationException("Download unvollständig.");
            await File.WriteAllBytesAsync(tmp, bytes);
        }

        var cmd = Path.Combine(Path.GetTempPath(), "knipser-update.cmd");
        var script =
            "@echo off\r\n" +
            "set /a n=0\r\n" +
            "ping 127.0.0.1 -n 3 >nul\r\n" +
            ":retry\r\n" +
            $"move /y \"{tmp}\" \"{target}\" >nul 2>&1\r\n" +
            "if errorlevel 1 (\r\n" +
            "  set /a n+=1\r\n" +
            "  if %n% GEQ 20 exit /b 1\r\n" +
            "  ping 127.0.0.1 -n 2 >nul\r\n" +
            "  goto retry\r\n" +
            ")\r\n" +
            $"start \"\" \"{target}\"\r\n" +
            "del \"%~f0\"\r\n";
        await File.WriteAllTextAsync(cmd, script);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { CreateNoWindow = true, UseShellExecute = false });
    }
}
