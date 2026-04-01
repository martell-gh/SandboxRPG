using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MTEngine.Rendering;

// один кадр анимации
public class AnimationFrame
{
    public int SrcX { get; set; }
    public int SrcY { get; set; }
    public int Width { get; set; } = 16;
    public int Height { get; set; } = 16;
    public float Duration { get; set; } = 0.1f; // секунды

    public Rectangle SourceRect => new Rectangle(SrcX, SrcY, Width, Height);
}

// один клип (idle, walk, attack и т.д.)
public class AnimationClip
{
    public string Name { get; set; } = "";
    public List<AnimationFrame> Frames { get; set; } = new();
    public bool Loop { get; set; } = true;

    public float TotalDuration => Frames.Sum(f => f.Duration);
}

// набор клипов для одного объекта
public class AnimationSet
{
    private readonly Dictionary<string, AnimationClip> _clips = new();
    public string TexturePath { get; set; } = "";

    public void AddClip(AnimationClip clip) => _clips[clip.Name] = clip;

    public AnimationClip? GetClip(string name)
        => _clips.TryGetValue(name, out var c) ? c : null;

    public IEnumerable<AnimationClip> GetAllClips() => _clips.Values;
    public bool HasClip(string name) => _clips.ContainsKey(name);

    // загрузка из animations.json
    public static AnimationSet? LoadFromFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            Console.WriteLine($"[AnimationSet] Not found: {jsonPath}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(jsonPath);
            var root = JsonNode.Parse(json)?.AsObject();
            if (root == null) return null;

            var set = new AnimationSet();

            // путь к текстуре (опционально — если у клипа своя текстура)
            set.TexturePath = root["texture"]?.GetValue<string>() ?? "";

            // парсим клипы
            var clipsNode = root["clips"]?.AsObject();
            if (clipsNode == null) return set;

            foreach (var clipPair in clipsNode)
            {
                var clipNode = clipPair.Value?.AsObject();
                if (clipNode == null) continue;

                var clip = new AnimationClip
                {
                    Name = clipPair.Key,
                    Loop = clipNode["loop"]?.GetValue<bool>() ?? true
                };

                var framesNode = clipNode["frames"]?.AsArray();
                if (framesNode == null) continue;

                foreach (var frameNode in framesNode)
                {
                    var f = frameNode?.AsObject();
                    if (f == null) continue;

                    clip.Frames.Add(new AnimationFrame
                    {
                        SrcX = f["srcX"]?.GetValue<int>() ?? 0,
                        SrcY = f["srcY"]?.GetValue<int>() ?? 0,
                        Width = f["width"]?.GetValue<int>() ?? 16,
                        Height = f["height"]?.GetValue<int>() ?? 16,
                        Duration = f["duration"]?.GetValue<float>() ?? 0.1f
                    });
                }

                if (clip.Frames.Count > 0)
                {
                    set.AddClip(clip);
                    Console.WriteLine($"[AnimationSet] Loaded clip: {clip.Name} ({clip.Frames.Count} frames)");
                }
            }

            return set;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[AnimationSet] Error loading {jsonPath}: {e.Message}");
            return null;
        }
    }
}

// воспроизводит анимацию — один инстанс на объект
public class AnimationPlayer
{
    private AnimationClip? _current;
    private float _elapsed;
    private int _frameIndex;
    private bool _finished;

    public AnimationClip? CurrentClip => _current;
    public AnimationFrame? CurrentFrame => _current?.Frames.Count > 0
        ? _current.Frames[_frameIndex] : null;
    public bool IsFinished => _finished;
    public bool IsPlaying => _current != null && !_finished;
    public string CurrentClipName => _current?.Name ?? "";

    public void Play(AnimationClip clip, bool restart = false)
    {
        if (_current == clip && !restart) return;
        _current = clip;
        _elapsed = 0f;
        _frameIndex = 0;
        _finished = false;
    }

    public void Play(AnimationSet set, string clipName, bool restart = false)
    {
        var clip = set.GetClip(clipName);
        if (clip == null)
        {
            Console.WriteLine($"[AnimationPlayer] Clip not found: {clipName}");
            return;
        }
        Play(clip, restart);
    }

    public void Stop()
    {
        _current = null;
        _elapsed = 0f;
        _frameIndex = 0;
        _finished = false;
    }

    public void Update(float deltaTime)
    {
        if (_current == null || _finished) return;
        if (_current.Frames.Count == 0) return;

        _elapsed += deltaTime;

        var frameDuration = _current.Frames[_frameIndex].Duration;
        if (_elapsed >= frameDuration)
        {
            _elapsed -= frameDuration;
            _frameIndex++;

            if (_frameIndex >= _current.Frames.Count)
            {
                if (_current.Loop)
                    _frameIndex = 0;
                else
                {
                    _frameIndex = _current.Frames.Count - 1;
                    _finished = true;
                }
            }
        }
    }

    // получить SourceRect для рисования
    public Rectangle? GetSourceRect()
    {
        return CurrentFrame?.SourceRect;
    }
}