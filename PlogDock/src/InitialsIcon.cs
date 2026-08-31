using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace PlogDock;

/// <summary>
/// The fallback tile: two letters on a colour derived from the plugin's internal
/// name. The derivation is a pure function of the name, so a plugin keeps the same
/// colour across sessions and stays recognisable by shape alone.
/// </summary>
internal static class InitialsIcon
{
    public static string Initials(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "??";

        var parts = displayName.Split(
            [' ', '-', '_', '.'],
            StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
            return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";

        var word = parts.Length == 1 ? parts[0] : displayName.Trim();

        return word.Length >= 2
            ? $"{char.ToUpperInvariant(word[0])}{char.ToLowerInvariant(word[1])}"
            : char.ToUpperInvariant(word[0]).ToString();
    }

    /// <summary>Background colour, packed for ImGui.</summary>
    public static uint Color(string internalName)
    {
        // FNV-1a: cheap, well spread, and stable across runtimes, unlike string
        // hashing which is randomised per process and would change the palette
        // on every launch.
        var hash = 2166136261u;

        unchecked
        {
            foreach (var c in internalName)
            {
                hash ^= c;
                hash *= 16777619u;
            }
        }

        var (r, g, b) = HsvToRgb((hash % 360u) / 360f, 0.52f, 0.62f);
        return ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, 1f));
    }

    /// <summary>Draws the tile at the cursor without advancing it, so a hit area
    /// can be laid over it.</summary>
    public static void Draw(string internalName, string displayName, float size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var corner = new Vector2(origin.X + size, origin.Y + size);

        drawList.AddRectFilled(origin, corner, Color(internalName), size * 0.18f);

        var text = Initials(displayName);
        var textSize = ImGui.CalcTextSize(text);
        var textPos = new Vector2(
            origin.X + ((size - textSize.X) / 2f),
            origin.Y + ((size - textSize.Y) / 2f));

        drawList.AddText(textPos, 0xFFFFFFFFu, text);
    }

    private static (float R, float G, float B) HsvToRgb(float h, float s, float v)
    {
        var sector = (int)(h * 6f);
        var offset = (h * 6f) - sector;

        var p = v * (1f - s);
        var q = v * (1f - (offset * s));
        var t = v * (1f - ((1f - offset) * s));

        return (sector % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
    }
}
