using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace KatrinasGame;

/// <summary>The worlds saved on this computer (user://saves/&lt;world&gt;.json), for the save-slot
/// list: each one's day, when it was last played, and your character in it.</summary>
public static class SaveSlots
{
    public const string Dir = "user://saves";
    /// <summary>Worlds the scripted tests make and throw away; never listed.</summary>
    private static readonly HashSet<string> Hidden = new() { "autotest", "autotest_mp" };

    public sealed record Slot(string World, int Day, DateTime Saved, int Level, string ClassId, int Players)
    {
        public string Summary(string player)
        {
            var parts = new List<string> { $"Day {Day}" };
            if (Level > 0) parts.Add($"{player}: level {Level}" + (PlayerClasses.IsValid(ClassId) ? $" {PlayerClasses.Get(ClassId).Name}" : ""));
            if (Players > 1) parts.Add($"{Players} adventurers");
            parts.Add(Saved == DateTime.MinValue ? "" : $"saved {Saved:d MMM yyyy, HH:mm}");
            return string.Join("  ·  ", parts.Where(p => p != ""));
        }
    }

    public static string PathOf(string world) => $"{Dir}/{world}.json";

    public static bool Exists(string world) => FileAccess.FileExists(PathOf(world));

    /// <summary>Every saved world, most recently played first. <paramref name="player"/> picks
    /// whose level and class to show.</summary>
    public static List<Slot> List(string player)
    {
        var slots = new List<Slot>();
        using var dir = DirAccess.Open(Dir);
        if (dir == null) return slots;
        foreach (var file in dir.GetFiles())
        {
            if (!file.EndsWith(".json")) continue;
            string world = file[..^5];
            if (Hidden.Contains(world)) continue;
            slots.Add(Read(world, player));
        }
        return slots.OrderByDescending(s => s.Saved).ToList();
    }

    private static Slot Read(string world, string player)
    {
        string path = PathOf(world);
        var saved = DateTime.MinValue;
        ulong mtime = FileAccess.GetModifiedTime(path);
        if (mtime > 0) saved = DateTimeOffset.FromUnixTimeSeconds((long)mtime).LocalDateTime;
        int day = 1, level = 0, players = 0;
        string cls = "";
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file != null && Json.ParseString(file.GetAsText()) is { VariantType: Variant.Type.Dictionary } parsed)
        {
            var data = parsed.AsGodotDictionary();
            if (data.TryGetValue("day", out var d)) day = d.AsInt32();
            if (data.TryGetValue("players", out var pv) && pv.VariantType == Variant.Type.Dictionary)
            {
                var all = pv.AsGodotDictionary();
                players = all.Count;
                // Your own character, or else whoever played most.
                Godot.Collections.Dictionary rec = null;
                if (all.TryGetValue(player, out var mine)) rec = mine.AsGodotDictionary();
                if (rec != null)
                {
                    level = Levels.LevelFor(rec.TryGetValue("xp", out var xp) ? xp.AsInt32() : 0);
                    cls = rec.TryGetValue("class", out var c) ? c.AsString() : "";
                }
            }
        }
        return new Slot(world, day, saved, level, cls, players);
    }

    /// <summary>Your class in a world, if it remembers one ("" otherwise).</summary>
    public static string ClassIn(string world, string player) => Exists(world) ? Read(world, player).ClassId : "";

    /// <summary>A world name that's safe as a file name: letters, digits, spaces, - and _.</summary>
    public static string Clean(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in (name ?? "").Trim())
            sb.Append(char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '_' ? ch : '_');
        string s = sb.ToString().Trim();
        if (s.Length > 40) s = s[..40];
        return s;
    }

    /// <summary>"World 2", "World 3"… the first unused one.</summary>
    public static string FreshName()
    {
        for (int i = 1; ; i++)
        {
            string name = i == 1 ? "My World" : $"My World {i}";
            if (!Exists(name)) return name;
        }
    }

    /// <summary>Move a world's save to user://saves/deleted (so a slip can still be undone by hand).</summary>
    public static bool Delete(string world)
    {
        if (!Exists(world)) return false;
        DirAccess.MakeDirRecursiveAbsolute($"{Dir}/deleted");
        string target = $"{Dir}/deleted/{world}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
        return DirAccess.RenameAbsolute(PathOf(world), target) == Error.Ok;
    }
}
