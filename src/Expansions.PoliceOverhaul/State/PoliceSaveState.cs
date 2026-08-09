using S1API.Internal.Abstraction;
using S1API.Saveables;

namespace Expansions.PoliceOverhaul.State;

/// <summary>
/// Per-save persistence for heat and outlaw status, written to
/// <c>&lt;save&gt;\Modded\Saveables\expansions_police.json</c>.
/// <para>
/// S1API discovers direct <see cref="Saveable"/> inheritors by reflection and constructs exactly one
/// of each, so this class is never instantiated by us and must stay one level deep. Deriving from
/// <c>ISaveable</c> by hand instead would put our serialisation inside the vanilla save coroutine,
/// where a throw can abort the player's save.
/// </para>
/// </summary>
public sealed class PoliceSaveState : Saveable
{
    private static PoliceSaveState? _live;

    /// <summary>Never rename: this string is the folder name inside the save.</summary>
    [SaveableField("expansions_police")]
    private PoliceSaveData _data = new();

    /// <summary>
    /// The instance S1API built, or null before the first save load. Null is normal and common —
    /// every caller has to cope with it rather than forcing one into existence.
    /// </summary>
    internal static PoliceSaveState? Live => _live;

    /// <summary>Raised after S1API has filled the record set, so the director can adopt it.</summary>
    internal static event Action<PoliceSaveState>? Loaded;

    public override SaveableLoadOrder LoadOrder => SaveableLoadOrder.AfterBaseGame;

    internal List<PlayerHeatRecord> Players => _data.Players ??= new List<PlayerHeatRecord>();

    /// <summary>Per-save PRNG seed for the event scheduler. 0 means "not initialised yet".</summary>
    internal int EventSeed
    {
        get => _data.EventSeed;
        set => _data.EventSeed = value;
    }

    internal int EventRolls
    {
        get => _data.EventRolls;
        set => _data.EventRolls = value;
    }

    internal int NextFederalCheckMinute
    {
        get => _data.NextFederalCheckMinute;
        set => _data.NextFederalCheckMinute = value;
    }

    internal int NextRaidCheckMinute
    {
        get => _data.NextRaidCheckMinute;
        set => _data.NextRaidCheckMinute = value;
    }

    internal PlayerHeatRecord GetOrCreate(string key, string displayName)
    {
        foreach (var record in Players)
        {
            if (string.Equals(record.PlayerKey, key, StringComparison.Ordinal))
            {
                if (displayName.Length > 0)
                    record.PlayerName = displayName;

                return record;
            }
        }

        var created = new PlayerHeatRecord { PlayerKey = key, PlayerName = displayName };
        Players.Add(created);
        return created;
    }

    /// <summary>
    /// Fires when S1API finishes constructing the singleton, which happens whether or not a save file
    /// exists. Publishing the instance here rather than waiting for a load is what lets a brand-new
    /// game accumulate heat into the object that will eventually be written.
    /// </summary>
    protected override void OnCreated()
    {
        _live = this;
        _data.Players ??= new List<PlayerHeatRecord>();
    }

    protected override void OnLoaded()
    {
        _live = this;
        _data.Players ??= new List<PlayerHeatRecord>();

        try
        {
            Loaded?.Invoke(this);
        }
        catch (Exception ex)
        {
            PoliceLog.Error("A heat-state load handler threw; heat will start from the file as read.", ex);
        }
    }

    protected override void OnSaved() => _live = this;

    /// <summary>
    /// One level of nesting under the saveable field. A bare <c>List&lt;T&gt;</c> would serialise as a
    /// naked JSON array, leaving nowhere to add a schema version later without breaking old saves.
    /// </summary>
    public sealed class PoliceSaveData
    {
        /// <summary>
        /// 3 added the event-scheduler seed and next-fire minutes. Purely additive: older files load
        /// with those fields at their defaults, and a newer file opened by an older build ignores them.
        /// </summary>
        public int Version = 3;

        public List<PlayerHeatRecord>? Players = new();

        /// <summary>Deterministic event-scheduler seed for this save. 0 = not rolled yet.</summary>
        public int EventSeed;

        /// <summary>How many scheduler rolls have been consumed, so reloads continue the sequence.</summary>
        public int EventRolls;

        /// <summary>Absolute in-game minute of the next federal eligibility check. -1 = none queued.</summary>
        public int NextFederalCheckMinute = -1;

        /// <summary>Absolute in-game minute of the next raid eligibility check. -1 = none queued.</summary>
        public int NextRaidCheckMinute = -1;
    }
}
