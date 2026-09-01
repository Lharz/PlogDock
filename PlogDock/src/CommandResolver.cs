using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Command;

namespace PlogDock;

/// <summary>
/// Attributes each registered command to the plugin that registered it, by reading
/// the declaring assembly of its Handler delegate. Exact match or nothing: no name
/// similarity, no parsing of plugin descriptions. A plugin PlogDock cannot attribute
/// a command to stays unreachable rather than wired to a guess.
/// </summary>
internal sealed class CommandResolver
{
    private Dictionary<string, string> byInternalName = new(StringComparer.Ordinal);

    /// <summary>The command opening this plugin, or null when none could be attributed.</summary>
    public string? ResolveFor(string internalName)
        => this.byInternalName.TryGetValue(internalName, out var command) ? command : null;

    public void Refresh()
    {
        var candidates = new Dictionary<string, List<KeyValuePair<string, IReadOnlyCommandInfo>>>(StringComparer.Ordinal);
        var total = 0;

        foreach (var pair in Service.Commands.Commands)
        {
            total++;

            string? assembly;
            try
            {
                assembly = pair.Value.Handler?.Method.DeclaringType?.Assembly.GetName().Name;
            }
            catch (Exception ex)
            {
                Service.Log.Debug(ex, "Could not read the handler of {Command}", pair.Key);
                continue;
            }

            if (string.IsNullOrEmpty(assembly))
                continue;

            // Dalamud derives a plugin's InternalName from its assembly name, so the
            // two match by construction.
            if (!candidates.TryGetValue(assembly, out var list))
                candidates[assembly] = list = [];

            list.Add(pair);
        }

        var fresh = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (assembly, list) in candidates)
        {
            var chosen = Choose(list);
            if (chosen is not null)
                fresh[assembly] = chosen;
        }

        this.byInternalName = fresh;

        // Debug rather than Information: this fires on every refresh, and a line per
        // plugin load in someone else's log is noise they did not ask for.
        Service.Log.Debug(
            "PlogDock: {Resolved} plugins reachable by command, out of {Assemblies} that registered any of {Total} commands",
            fresh.Count,
            candidates.Count,
            total);
    }

    /// <summary>
    /// Picks the command that opens a plugin, among those it registered.
    /// <para>
    /// Only commands the author exposed in the in-game help are eligible. A hidden
    /// command is an internal entry point, not a way in: PandorasBox registers
    /// thirteen commands and hides all but <c>/pandora</c>, so honouring ShowInHelp
    /// is reading the author's own declaration rather than guessing.
    /// </para>
    /// <para>
    /// Remaining ties are broken deterministically by DisplayOrder, then by length,
    /// then alphabetically. Dictionary enumeration order is arbitrary and must never
    /// decide which command a button fires.
    /// </para>
    /// </summary>
    private static string? Choose(List<KeyValuePair<string, IReadOnlyCommandInfo>> commands)
        => commands
            .Where(c => c.Value.ShowInHelp)
            .OrderBy(c => c.Value.DisplayOrder)
            .ThenBy(c => c.Key.Length)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => c.Key)
            .FirstOrDefault();
}
