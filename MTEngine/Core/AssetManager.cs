using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;

namespace MTEngine.Core;

public class AssetManager
{
    public GraphicsDevice GraphicsDevice => _graphics;

    private readonly ContentManager _content;
    private readonly GraphicsDevice _graphics;
    private readonly Dictionary<string, Texture2D> _textures = new();

    public AssetManager(ContentManager content, GraphicsDevice graphics)
    {
        _content = content;
        _graphics = graphics;
    }

    public Texture2D GetTexture(string name)
    {
        if (_textures.TryGetValue(name, out var tex))
            return tex;

        var loaded = _content.Load<Texture2D>(name);
        _textures[name] = loaded;
        return loaded;
    }

    public Texture2D? LoadFromFile(string filePath)
    {
        if (_textures.TryGetValue(filePath, out var cached))
            return cached;

        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[AssetManager] File not found: {filePath}");
            return null;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var tex = Texture2D.FromStream(_graphics, stream);
            _textures[filePath] = tex;
            Console.WriteLine($"[AssetManager] Loaded: {filePath}");
            return tex;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[AssetManager] Failed to load {filePath}: {e.Message}");
            return null;
        }
    }

    public Texture2D GetColorTexture(string colorHex)
    {
        if (_textures.TryGetValue(colorHex, out var existing))
            return existing;

        var color = HexToColor(colorHex);
        var tex = new Texture2D(_graphics, 32, 32);
        var pixels = new Color[32 * 32];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;
        tex.SetData(pixels);
        _textures[colorHex] = tex;
        return tex;
    }

    private static Color HexToColor(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToInt32(hex[..2], 16);
        var g = Convert.ToInt32(hex[2..4], 16);
        var b = Convert.ToInt32(hex[4..6], 16);
        return new Color(r, g, b);
    }

    public void UnloadAll()
    {
        _textures.Clear();
        _content.Unload();
    }
}