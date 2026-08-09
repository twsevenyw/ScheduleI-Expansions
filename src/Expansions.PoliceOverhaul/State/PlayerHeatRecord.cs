namespace Expansions.PoliceOverhaul.State;

/// <summary>
/// Everything the mod remembers about one player, across sleep, save and reload.
/// <para>
/// Newtonsoft serialises this by public field, so field names are the on-disk schema: renaming one
/// silently drops that player's progress. Add, never rename.
/// </para>
/// </summary>
public sealed class PlayerHeatRecord
{
    /// <summary>See <c>PlayerKeys</c> — the code, or <c>"local"</c> in a solo game.</summary>
    public string PlayerKey = "local";

    /// <summary>Last known display name, so the menu can say who a record belongs to.</summary>
    public string PlayerName = string.Empty;

    public float Heat;

    public int OutlawTier;

    /// <summary>Consecutive in-game days with no crime recorded and no arrest.</summary>
    public int CleanDayStreak;

    /// <summary>Set by any crime or arrest, cleared at the day rollover. Persisted so a save taken
    /// mid-day cannot launder a dirty day into a clean one.</summary>
    public bool DirtyToday;

    /// <summary>Elapsed-day stamps of recent arrests; trimmed to the last seven days on write.</summary>
    public List<int> ArrestDays = new();

    /// <summary>
    /// Running dollar total of everything the police have taken off this player — fines charged plus
    /// debt raised. Feeds the "you are moving more money than a normal person" federal trigger, so it
    /// counts only amounts we know exactly rather than guessing at the worth of a seized stash.
    /// </summary>
    public float PoliceTakeTotal;

    /// <summary>Tracked minutes spent at or above the federal heat threshold.</summary>
    public int MinutesAtFederalHeat;

    /// <summary>Minutes this day with nothing on the rap sheet, so sleep decay can net off live drain.</summary>
    public int MinutesCleanToday;

    /// <summary>Heat gained from contract payments today, against the daily cap.</summary>
    public float DealHeatToday;

    /// <summary>Elapsed day the last federal event ended, for the cooldown. -1 means never.</summary>
    public int LastFederalEventDay = -1;

    public int FederalEncounters;

    /// <summary>Arrests served since the outlaw status latched; two of them promote Marked to Hunted.</summary>
    public int ArrestsWhileOutlaw;

    internal OutlawTier Outlaw
    {
        get => (OutlawTier)Math.Clamp(OutlawTier, 0, 2);
        set => OutlawTier = (int)value;
    }

    internal HeatTier Tier => HeatModel.TierFor(Heat);

    /// <summary>Arrests inside the shipped seven-day order cycle, which is a federal trigger.</summary>
    internal int ArrestsInLastWeek(int elapsedDays)
    {
        var count = 0;
        for (var i = 0; i < ArrestDays.Count; i++)
        {
            if (elapsedDays - ArrestDays[i] < 7)
                count++;
        }

        return count;
    }

    internal void RecordArrest(int elapsedDays)
    {
        ArrestDays.Add(elapsedDays);
        ArrestDays.RemoveAll(day => elapsedDays - day >= 7);
    }
}
