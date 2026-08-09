using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Il2CppScheduleOne.Quests;
using MelonLoader;
using S1API.Console;

namespace CreativeMode;

internal sealed class QuestEntry
{
    public string Title { get; init; } = "";
    public string Code { get; init; } = "";
    public string State { get; init; } = "";
}

internal static class QuestCatalog
{
    private static readonly (string Title, string Code)[] FallbackQuests =
    {
        ("Welcome to Hyland Point", "Welcome_to_Hyland_Point"),
        ("Getting Started", "Getting_Started"),
        ("Gearing Up", "Gearing_Up"),
        ("Packin'", "Packin"),
        ("Keeping it Fresh", "Keeping_it_Fresh"),
        ("On the Grind", "On_the_Grind"),
        ("Moving Up", "Moving_Up"),
        ("Dodgy Dealing", "Dodgy_Dealing"),
        ("Mixing Mania", "Mixing_Mania"),
        ("Needin' the Green", "Needing_the_Green"),
        ("Wretched Hive of Scum and Villainy", "Wretched_Hive_of_Scum_and_Villainy"),
        ("We Need to Cook", "We_Need_to_Cook"),
        ("Vibin' on the 'Cybin", "Vibin_on_the_Cybin"),
        ("Cleaners", "Cleaners"),
        ("Botanists", "Botanists"),
        ("Packagers", "Packagers"),
        ("Chemists", "Chemists"),
        ("Money Management", "Money_Management"),
        ("Clean Cash", "Clean_Cash"),
        ("Finishing the Job", "Finishing_the_Job"),
        ("Connections", "Connections"),
        ("Down to Business", "Down_to_Business"),
        ("Employees", "Employees"),
        ("Expanding Operations", "Expanding_Operations"),
        ("Securing Supplies", "Securing_Supplies"),
        ("Sink or Swim", "Sink_or_Swim"),
        ("The Deep End", "The_Deep_End"),
        ("Warehouse", "Warehouse"),
        ("Deal for Cartel", "Deal_for_Cartel"),
        ("Defeat Cartel", "Defeat_Cartel"),
        ("Unfavourable Agreements", "Unfavourable_Agreements"),
    };

    public static List<QuestEntry> Build(MelonLogger.Instance log)
    {
        var map = new Dictionary<string, QuestEntry>(StringComparer.OrdinalIgnoreCase);

        void Add(string title, string code, string state)
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(code))
                return;
            var key = !string.IsNullOrWhiteSpace(title) ? title : code;
            map[key] = new QuestEntry
            {
                Title = string.IsNullOrWhiteSpace(title) ? code : title,
                Code = string.IsNullOrWhiteSpace(code) ? Sanitize(title) : code,
                State = state
            };
        }

        try
        {
            var quests = Quest.Quests;
            if (quests != null)
            {
                for (var i = 0; i < quests.Count; i++)
                {
                    var q = quests[i];
                    if (q == null)
                        continue;
                    string title = "";
                    string code = "";
                    string state = "";
                    try { title = q.Title ?? ""; } catch { /* ignore */ }
                    try { code = q.name ?? ""; } catch { /* ignore */ }
                    try { state = q.State.ToString(); } catch { /* ignore */ }
                    if (string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(title))
                        code = Sanitize(title);
                    Add(title, code, state);
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning($"Live Quest.Quests failed: {ex.Message}");
        }

        foreach (var (title, code) in FallbackQuests)
        {
            if (!map.ContainsKey(title))
                Add(title, code, "");
        }

        var list = map.Values
            .OrderBy(q => q.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        log.Msg($"Quest catalog built: {list.Count} quests.");
        return list;
    }

    public static void Complete(QuestEntry entry, MelonLogger.Instance log)
    {
        var nativeOk = TryCompleteNative(entry, log);

        // Console wants numeric state (2 = completed), not the enum name.
        foreach (var code in GetCodes(entry))
        {
            try { ConsoleHelper.Submit($"setqueststate {code} 2"); }
            catch (Exception ex) { log.Warning($"setqueststate {code} failed: {ex.Message}"); }

            // Also complete a bunch of entry indices — harmless if missing.
            for (var i = 0; i < 12; i++)
            {
                try { ConsoleHelper.Submit($"setquestentrystate {code} {i} 2"); }
                catch { /* ignore */ }
            }
        }

        if (nativeOk)
            log.Msg($"Completed quest: {entry.Title}");
        else
            log.Warning($"Native complete miss for '{entry.Title}' — console force-complete sent.");
    }

    public static void CompleteAll(IEnumerable<QuestEntry> entries, MelonLogger.Instance log)
    {
        foreach (var e in entries)
        {
            try { Complete(e, log); }
            catch (Exception ex) { log.Warning($"Complete '{e.Title}' failed: {ex.Message}"); }
        }
    }

    private static bool TryCompleteNative(QuestEntry entry, MelonLogger.Instance log)
    {
        try
        {
            var quests = Quest.Quests;
            if (quests == null)
                return false;

            for (var i = 0; i < quests.Count; i++)
            {
                var q = quests[i];
                if (q == null || !Matches(q, entry))
                    continue;

                CompleteEntries(q, log);
                return InvokeComplete(q, log);
            }
        }
        catch (Exception ex)
        {
            log.Warning($"TryCompleteNative failed: {ex.Message}");
        }

        return false;
    }

    private static void CompleteEntries(Quest q, MelonLogger.Instance log)
    {
        try
        {
            var entriesProp = q.GetType().GetProperty("Entries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var entries = entriesProp?.GetValue(q);
            if (entries == null)
                return;

            var countProp = entries.GetType().GetProperty("Count");
            var count = countProp != null ? Convert.ToInt32(countProp.GetValue(entries)) : 0;
            var indexer = entries.GetType().GetProperty("Item");

            for (var e = 0; e < count; e++)
            {
                object? qe = null;
                try { qe = indexer?.GetValue(entries, new object[] { e }); } catch { /* */ }
                if (qe == null)
                    continue;

                InvokeAny(qe, log, "SetState", EQuestState.Completed);
                InvokeAny(qe, log, "Complete");
                InvokeAny(q, log, "SetQuestEntryState", e, EQuestState.Completed, true);
                InvokeAny(q, log, "SetQuestEntryState", e, EQuestState.Completed);
            }
        }
        catch (Exception ex)
        {
            log.Warning($"CompleteEntries failed: {ex.Message}");
        }
    }

    private static bool InvokeComplete(Quest q, MelonLogger.Instance log)
    {
        if (InvokeAny(q, log, "SetQuestState", EQuestState.Completed))
            return true;
        if (InvokeAny(q, log, "SetQuestState", EQuestState.Completed, true))
            return true;
        if (InvokeAny(q, log, "SetState", EQuestState.Completed))
            return true;
        if (InvokeAny(q, log, "SetState", EQuestState.Completed, true))
            return true;
        if (InvokeAny(q, log, "Complete", true))
            return true;
        if (InvokeAny(q, log, "Complete"))
            return true;
        log.Warning("No working Complete/SetQuestState overload found.");
        return false;
    }

    private static bool InvokeAny(object target, MelonLogger.Instance log, string name, params object[] args)
    {
        try
        {
            var methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == name)
                .ToArray();
            foreach (var m in methods)
            {
                var ps = m.GetParameters();
                if (ps.Length != args.Length)
                    continue;
                try
                {
                    m.Invoke(target, args);
                    return true;
                }
                catch
                {
                    // try next overload
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning($"Invoke {name} failed: {ex.Message}");
        }
        return false;
    }

    private static bool Matches(Quest q, QuestEntry entry)
    {
        string title = "";
        string name = "";
        try { title = q.Title ?? ""; } catch { /* */ }
        try { name = q.name ?? ""; } catch { /* */ }

        return title.Equals(entry.Title, StringComparison.OrdinalIgnoreCase)
               || name.Equals(entry.Code, StringComparison.OrdinalIgnoreCase)
               || name.Equals(entry.Title, StringComparison.OrdinalIgnoreCase)
               || Sanitize(title).Equals(Sanitize(entry.Title), StringComparison.OrdinalIgnoreCase)
               || Sanitize(name).Equals(Sanitize(entry.Code), StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetCodes(QuestEntry entry)
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void add(string? s)
        {
            if (!string.IsNullOrWhiteSpace(s))
                codes.Add(s.Trim());
        }

        add(entry.Code);
        add(entry.Title);
        add(Sanitize(entry.Title));
        add(entry.Title.Replace(' ', '_'));
        add(entry.Title.Replace("'", "").Replace("'", "").Replace(' ', '_'));
        if (entry.Code.StartsWith("Quest_", StringComparison.OrdinalIgnoreCase))
            add(entry.Code["Quest_".Length..]);
        return codes;
    }

    private static string Sanitize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "";
        var s = title.Trim();
        s = s.Replace("'", "").Replace("'", "").Replace("'", "");
        s = s.Replace(' ', '_');
        while (s.Contains("__"))
            s = s.Replace("__", "_");
        return s;
    }
}
