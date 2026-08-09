using Expansions.Core.Tutorial.Chapters;

namespace Expansions.Core.Tutorial;

/// <summary>
/// The fixed set of quest slots the line is played through, and the chapter id each one hosts.
/// <para>
/// A slot exists because S1API identifies a saved mod quest by its runtime class name: the class list
/// below is effectively the on-disk schema, so the names must never change and the slots cannot be
/// generated at runtime. Chapters map onto slots by id, and anything registered under an id Core does
/// not know about is played in <see cref="Extras"/>, so a fourth mod still gets into the line without
/// a new class.
/// </para>
/// </summary>
internal static class TutorialSlots
{
    public const string Welcome = "welcome";
    public const string Modules = "modules";
    public const string Diagnostics = "diagnostics";
    public const string CreativeMode = "creative_mode";
    public const string Drivers = "drivers";
    public const string Police = "police";
    public const string Customers = "customers";
    public const string Extras = "extras";

    /// <summary>Play order. <see cref="Extras"/> is last so late arrivals never reshuffle the line.</summary>
    public static readonly string[] Ordered =
    {
        Welcome, Modules, Diagnostics, CreativeMode, Drivers, Police, Customers, Extras,
    };

    /// <summary>Chapter id to slot. Ids not listed here fall through to <see cref="Extras"/>.</summary>
    private static readonly Dictionary<string, string> BySlotChapter = new(StringComparer.OrdinalIgnoreCase)
    {
        [TutorialChapters.MenuId] = Welcome,
        [TutorialChapters.ModulesId] = Modules,
        [TutorialChapters.DiagnosticsId] = Diagnostics,
        [TutorialChapters.CreativeModeId] = CreativeMode,
        [TutorialChapters.DriversId] = Drivers,
        [TutorialChapters.PoliceId] = Police,
        [TutorialChapters.CustomersId] = Customers,
    };

    public static string SlotFor(string chapterId) =>
        BySlotChapter.TryGetValue(chapterId, out var slot) ? slot : Extras;

    /// <summary>The concrete quest type a slot is played with, or null if the slot is unknown.</summary>
    public static Type? QuestTypeFor(string slot) => slot switch
    {
        Welcome => typeof(ExpansionsWelcomeQuest),
        Modules => typeof(ExpansionsModulesQuest),
        Diagnostics => typeof(ExpansionsDiagnosticsQuest),
        CreativeMode => typeof(ExpansionsCreativeModeQuest),
        Drivers => typeof(ExpansionsDriversQuest),
        Police => typeof(ExpansionsPoliceQuest),
        Customers => typeof(ExpansionsCustomersQuest),
        Extras => typeof(ExpansionsExtrasQuest),
        _ => null,
    };
}

internal sealed class ExpansionsWelcomeQuest : TutorialQuest
{
    internal override string SlotKey => TutorialSlots.Welcome;
}

internal sealed class ExpansionsModulesQuest : TutorialQuest
{
    internal override string SlotKey => TutorialSlots.Modules;
}

internal sealed class ExpansionsDiagnosticsQuest : TutorialQuest
{
    internal override string SlotKey => TutorialSlots.Diagnostics;
}

internal sealed class ExpansionsCreativeModeQuest : TutorialQuest
{
    internal override string SlotKey => TutorialSlots.CreativeMode;
}

internal sealed class ExpansionsDriversQuest : TutorialQuest
{
    internal override string SlotKey => TutorialSlots.Drivers;
}

internal sealed class ExpansionsPoliceQuest : TutorialQuest
{
    internal override string SlotKey => TutorialSlots.Police;
}

internal sealed class ExpansionsCustomersQuest : TutorialQuest
{
    internal override string SlotKey => TutorialSlots.Customers;
}

internal sealed class ExpansionsExtrasQuest : TutorialQuest
{
    internal override string SlotKey => TutorialSlots.Extras;
}
