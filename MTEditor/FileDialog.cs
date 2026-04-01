using System;
using System.Diagnostics;

namespace MTEditor;

public static class FileDialog
{
    // открыть файл
    public static string? OpenFile(string defaultPath)
    {
        var script = $@"tell application ""System Events""
            set theFile to choose file with prompt ""Select map file"" default location ""{defaultPath}"" of type {{""json""}}
            return POSIX path of theFile
        end tell";

        return RunAppleScript(script);
    }

    // сохранить файл
    public static string? SaveFile(string defaultPath, string defaultName)
    {
        var script = $@"tell application ""System Events""
            set theFile to choose file name with prompt ""Save map as"" default location ""{defaultPath}"" default name ""{defaultName}""
            return POSIX path of theFile
        end tell";

        return RunAppleScript(script);
    }

    private static string? RunAppleScript(string script)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "osascript",
                Arguments = $"-e '{script}'",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[FileDialog] Error: {e.Message}");
            return null;
        }
    }
}