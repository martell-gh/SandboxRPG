using System;
using System.Reflection;
using Microsoft.Xna.Framework;

namespace MTEngine.Core;

/// <summary>
/// Forces SDL2 to create the game window with the AllowHighDPI flag so the
/// back buffer can match the physical display resolution on Retina / HiDPI
/// monitors. Without this MonoGame caps the back buffer at the OS "scaled"
/// resolution (e.g., a 16" MBP M2 Pro reports 1728×1117 instead of its real
/// 3456×2234), which is exactly what the user sees in the resolution dropdown.
///
/// The hook patches SDL_CreateWindow via reflection because MonoGame doesn't
/// expose the SDL window creation flags directly.
/// </summary>
public static class HighDpiBootstrap
{
    private static bool _installed;
    private static Delegate? _originalCreateWindow;
    private static int _allowHighDpiFlag;

    public static void TryEnable()
    {
        if (_installed)
            return;

        _installed = true;

        try
        {
            Environment.SetEnvironmentVariable("SDL_VIDEO_HIGHDPI_DISABLED", "0");

            var assembly = typeof(Game).Assembly;
            var windowType = assembly.GetType("Sdl+Window");
            var windowStateType = assembly.GetType("Sdl+Window+State");
            if (windowType == null || windowStateType == null)
                return;

            var createWindowField = windowType.GetField("SDL_CreateWindow", BindingFlags.Static | BindingFlags.NonPublic);
            var allowHighDpiField = windowStateType.GetField("AllowHighDPI", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (createWindowField?.GetValue(null) is not Delegate originalCreateWindow || allowHighDpiField?.GetValue(null) is not int allowHighDpiFlag)
                return;

            _originalCreateWindow = originalCreateWindow;
            _allowHighDpiFlag = allowHighDpiFlag;

            var replacementMethod = typeof(HighDpiBootstrap).GetMethod(nameof(CreateWindowWithHighDpi), BindingFlags.Static | BindingFlags.NonPublic);
            if (replacementMethod == null)
                return;

            var replacementDelegate = replacementMethod.CreateDelegate(createWindowField.FieldType);
            createWindowField.SetValue(null, replacementDelegate);
        }
        catch
        {
            // If MonoGame internals differ on some platform/runtime, continue without crashing the app.
        }
    }

    private static IntPtr CreateWindowWithHighDpi(string title, int x, int y, int width, int height, int flags)
    {
        if (_originalCreateWindow == null)
            return IntPtr.Zero;

        var result = _originalCreateWindow.DynamicInvoke(title, x, y, width, height, flags | _allowHighDpiFlag);
        return result is IntPtr handle ? handle : IntPtr.Zero;
    }
}
