using MTEngine.Core;

namespace MTEditor;

internal static class Program
{
    private static void Main()
    {
        HighDpiBootstrap.TryEnable();

        using var game = new EditorGame();
        game.Run();
    }
}
