using Engine.Core;

namespace Engine.Editor.Models;

public sealed class ConsoleFilterModel
{
    private readonly HashSet<LogLevel> _levels = Enum.GetValues<LogLevel>().ToHashSet();

    public string Category { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public IReadOnlyCollection<LogLevel> Levels => _levels;

    public bool IsLevelEnabled(LogLevel level) => _levels.Contains(level);

    public void SetLevelEnabled(LogLevel level, bool enabled)
    {
        if (enabled)
            _levels.Add(level);
        else
            _levels.Remove(level);
    }

    public void EnableAllLevels()
    {
        _levels.Clear();
        foreach (LogLevel level in Enum.GetValues<LogLevel>())
            _levels.Add(level);
    }

    public bool Matches(in LogEntry entry)
    {
        if (!_levels.Contains(entry.Level))
            return false;
        string category = Category?.Trim() ?? string.Empty;
        if (category.Length > 0 && !entry.Category.Contains(category, StringComparison.OrdinalIgnoreCase))
            return false;
        string text = Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return true;
        return entry.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
            || entry.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
            || (entry.Exception?.ToString().Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public IEnumerable<LogEntry> Apply(IEnumerable<LogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Where(entry => Matches(entry));
    }
}
