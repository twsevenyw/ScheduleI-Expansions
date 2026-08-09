using Expansions.SpecialCustomers.Archetypes;
using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Detection;
using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Persistence;
using Expansions.SpecialCustomers.Visitors;
using S1API.GameTime;
using S1API.Map;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>
/// The one thing that decides when a group turns up, what it does while it is here, and that it
/// leaves cleanly.
/// <para>
/// Nothing is polled per frame except the arrival stagger. Everything else hangs off the game's own
/// hour and day events, plus the sleep-end event so a player who slept through a departure does not
/// wake up to a group that never left.
/// </para>
/// </summary>
internal sealed class VisitDirector
{
    /// <summary>
    /// After this many failed attempts the visit stops asking. The failure is a state the group's
    /// own hourly tick cannot fix — no product listed, a locked customer, a delivery location the
    /// game will not resolve — so retrying every hour just fills the log.
    /// </summary>
    private const int MaxOfferAttempts = 3;

    /// <summary>Every slot, used to hold the watchdog off entirely while a reveal is in flight.</summary>
    private static readonly int[] AllSlotIndices = VisitorSlot.All.Select(slot => slot.Index).ToArray();

    private readonly ArrivalStagger _stagger = new();
    private readonly object _gate = new();

    private VisitStateData _state = new();
    private VisitStateData? _pendingRestore;
    private Visit? _visit;
    private bool _attached;
    private bool _poolParked;

    private Action? _hourHandler;
    private Action? _dayHandler;
    private Action<int>? _sleepHandler;
    private Action<VisitStateData>? _stateHandler;
    private Action? _poolHandler;

    internal Visit? Current => _visit;

    internal VisitStateData State => _state;

    /// <summary>Absolute elapsed day the next group is due, or 0 before anything is scheduled.</summary>
    internal int NextVisitDay => _state.NextVisitDay;

    internal string LastActionNote { get; private set; } = string.Empty;

    /// <summary>Wires the clock and save hooks. Returns the teardown the module hands to its lifetime.</summary>
    internal Action Attach()
    {
        Detach();

        _hourHandler = () => Guard("the hourly tick", () => Tick());
        _dayHandler = () => Guard("the daily tick", () => Tick());
        _sleepHandler = _ => Guard("waking up", () => Tick());
        _stateHandler = data => Guard("restoring the saved group", () => Restore(data));
        _poolHandler = () => Guard("binding the visitor pool", OnPoolSettled);

        TimeManager.OnHourPass += _hourHandler;
        TimeManager.OnDayPass += _dayHandler;
        TimeManager.OnSleepEnd += _sleepHandler;
        SpecialCustomerState.Loaded += _stateHandler;
        VisitorRuntime.PoolSettled += _poolHandler;

        _attached = true;

        // Enabling mid-session must not wait for the next load: if a save is already up, adopt it.
        // PoolSettled has already fired in that case and will not fire again, so the watchdog has to
        // be armed here or it would sit idle for the rest of the session.
        if (SpecialCustomerState.Current is not null)
        {
            Guard("adopting the loaded save", () => Restore(SpecialCustomerState.Snapshot()));

            if (VisitorRuntime.PoolReady)
                PostWatch.Arm();
        }

        return () => Guard("shutting down", Shutdown);
    }

    /// <summary>
    /// The per-frame work: the arrival reveal while one is in flight, and the parked-visitor
    /// watchdog, which is a distance compare per idle slot a few times a second.
    /// </summary>
    internal void Pump()
    {
        if (!_attached)
            return;

        _stagger.Pump();

        // Anyone in the current group is excluded: they were deliberately placed in a ring around
        // the meeting point, and the leader is allowed to walk to a handover.
        PostWatch.Pump(_stagger.IsActive ? AllSlotIndices : BusySlots());
    }

    private IReadOnlyList<int> BusySlots()
    {
        Visit? visit;
        lock (_gate)
            visit = _visit;

        return visit is null ? Array.Empty<int>() : visit.MemberSlots;
    }

    /// <summary>
    /// Starts a visit right now, ignoring the countdown. The menu's "Force a group arrival" action
    /// and the console command both land here.
    /// </summary>
    internal bool ForceVisit(string? archetypeId, out string message)
    {
        if (!HostGate.Evaluate(out var authority))
        {
            message = $"Only the host can bring a group into town ({authority}).";
            return false;
        }

        lock (_gate)
        {
            if (_visit is not null)
            {
                message = $"{_visit.Archetype.DisplayName} is already in town until {GameClock.Format(_visit.DepartureTime)}. Send them home first.";
                return false;
            }
        }

        var archetype = ArchetypeCatalog.Find(archetypeId) ?? PickArchetype();
        if (archetype is null)
        {
            message = "Every archetype is switched off in the config, so there is nobody to bring in.";
            return false;
        }

        if (!Begin(archetype, out message))
            return false;

        var visit = _visit!;
        var names = string.Join(", ", visit.Members.Select(slot => slot.FullName));
        message =
            $"{archetype.DisplayName} arrived in {WorldGeography.NameOf(visit.Region)} at {visit.DeliveryLocationName} " +
            $"with {visit.MemberSlots.Count} member(s): {names}. " +
            $"Leader {visit.Leader.FullName} texts at arrival; bulk order at {GameClock.Format(visit.OrderTime)}; " +
            $"they leave at {GameClock.Format(visit.DepartureTime)}. " +
            $"Pool ready: {VisitorRuntime.ResolvedCount()}/{VisitorSlot.Count}.";
        LastActionNote = message;
        return true;
    }

    /// <summary>Sends the group's bulk offer immediately rather than waiting for their order time.</summary>
    internal bool ForceOffer(out string message)
    {
        Visit? visit;
        lock (_gate)
            visit = _visit;

        if (visit is null)
        {
            message = "No group is in town, so there is nobody to make an offer.";
            return false;
        }

        if (!HostGate.Evaluate(out var authority))
        {
            message = $"Only the host can author an offer ({authority}).";
            return false;
        }

        visit.OfferAttempts++;

        var result = OfferFactory.Send(visit, out var failure);
        if (!result.Sent)
        {
            Persist();
            message = $"The offer could not be made: {failure}";
            return false;
        }

        visit.OfferSent = true;
        Persist();

        message = $"{visit.Archetype.DisplayName} offered ${result.Payment:0} for {result.Quantity} x {result.Products} " +
                  $"({result.MinQuality} or better). Check your phone.";
        LastActionNote = message;
        return true;
    }

    /// <summary>Ends the current visit early. Used by the menu and by the self-disable path.</summary>
    internal bool SendHome(out string message)
    {
        Visit? visit;
        lock (_gate)
            visit = _visit;

        if (visit is null)
        {
            message = "No group is in town.";
            return false;
        }

        End(visit, silent: false, reschedule: true);
        message = $"{visit.Archetype.DisplayName} left town.";
        LastActionNote = message;
        return true;
    }

    /// <summary>A one-screen answer to "what is this module doing right now?".</summary>
    internal IReadOnlyList<string> DescribeStatus()
    {
        var lines = new List<string>(10);
        var verdict = OfficialFeatureDetector.Verdict;

        lines.Add(verdict.IsEnabled
            ? $"Detection: active (score {verdict.Score}, {verdict.Mode})."
            : $"Detection: SELF-DISABLED — {verdict.Reason}");

        Visit? visit;
        lock (_gate)
            visit = _visit;

        if (visit is null)
        {
            var today = GameClock.ElapsedDays;
            var wait = _state.NextVisitDay - today;
            lines.Add(wait > 0
                ? $"No group in town. Next arrival on day {_state.NextVisitDay} ({wait} day(s) away) at {GameClock.Format(CustomerSettings.ArrivalTime)}."
                : $"No group in town. The next arrival is due (day {_state.NextVisitDay}, today is {today}).");
        }
        else
        {
            lines.Add($"{visit.Archetype.DisplayName} ({visit.Archetype.ShortName}) are in {WorldGeography.NameOf(visit.Region)}.");
            lines.Add($"  Meeting point: {visit.DeliveryLocationName}.");
            lines.Add($"  Members: {string.Join(", ", visit.Members.Select(m => m.FullName))}.");
            lines.Add($"  Leader: {visit.Leader.FullName}.");
            lines.Add($"  Order time {GameClock.Format(visit.OrderTime)}, leaving {GameClock.Format(visit.DepartureTime)} on day {visit.DepartureDay}.");
            lines.Add(visit.OfferSent ? "  The bulk offer has been sent." : "  The bulk offer has not been sent yet.");
            lines.Add($"  Dialogue entries attached: {VisitorDialogue.AttachedCount}.");

            var link = CustomerLink.Resolve(visit.Leader.Id, out var linkFailure);
            lines.Add(link is null
                ? $"  Leader's Customer component: unreachable ({linkFailure})."
                : $"  Leader holds: {(link.HasOffer ? $"an unanswered ${link.OfferPayment:0} offer" : "no offer")}, " +
                  $"{(link.HasContract ? $"a live contract ({link.ContractState})" : "no contract")}; " +
                  $"{(link.IsUnlocked ? "unlocked" : "LOCKED")}.");

            if (_stagger.IsActive)
                lines.Add($"  {_stagger.Pending} member(s) still arriving.");
        }

        lines.Add($"Pool: {VisitorRuntime.ResolvedCount()} of {VisitorSlot.Count} visitors are in the world.");
        lines.Add($"Last archetype: {(_state.LastArchetypeId.Length > 0 ? _state.LastArchetypeId : "none yet")}.");

        if (OfferFactory.LastFailure.Length > 0)
            lines.Add($"Last offer problem: {OfferFactory.LastFailure}");

        return lines;
    }

    /// <summary>
    /// Runs the full departure unwind, whatever state the visit is in. Registered up front so a
    /// disable — manual, from Core's error path, or from detection — always leaves the world clean.
    /// </summary>
    internal void Unwind(bool silent)
    {
        Visit? visit;
        lock (_gate)
            visit = _visit;

        if (visit is not null)
            End(visit, silent, reschedule: false);

        _stagger.Clear();
        VisitorDialogue.Detach();
        VisitAnnouncer.HideMarker();
        VisitorDresser.Clear();
        CustomerTuner.Forget();
    }

    /// <summary>
    /// Drops every offer the pool is still holding, and optionally the contracts too. Exposed as a
    /// menu action and as a dialogue choice on the leader, because a save that was already wedged
    /// before this fix shipped has no other way out.
    /// </summary>
    internal bool ClearOfferState(out string message)
    {
        if (!HostGate.Evaluate(out var authority))
        {
            message = $"Only the host can clear a group's order ({authority}).";
            return false;
        }

        var report = ClearPoolOfferState(includeContracts: true);

        Visit? visit;
        lock (_gate)
            visit = _visit;

        if (visit is not null)
        {
            visit.OfferSent = false;
            visit.OfferAttempts = 0;
            Persist();
        }

        message = report.Problems.Count > 0
            ? $"Cleared {report.Offers} offer(s) and {report.Contracts} contract(s), but: {string.Join("; ", report.Problems)}"
            : report.Offers + report.Contracts == 0
                ? "Nothing was stuck — no visitor is holding an offer or a contract."
                : $"Cleared {report.Offers} stuck offer(s) and {report.Contracts} contract(s). " +
                  (visit is not null ? "Use \"Make the group offer now\" to send a fresh one." : "The next group will start clean.");

        LastActionNote = message;
        return report.Problems.Count == 0;
    }

    /// <summary>
    /// A stage-by-stage answer to "why can I not sell to these people?", written for someone who
    /// cannot read the console.
    /// </summary>
    internal string DescribeSaleLoop()
    {
        Visit? visit;
        lock (_gate)
            visit = _visit;

        if (visit is null)
            return "No group is in town, so there is no sale to be stuck. " + NextArrivalLine();

        var lines = new List<string>(10)
        {
            $"{visit.Archetype.DisplayName} are in {WorldGeography.NameOf(visit.Region)}, meeting at {visit.DeliveryLocationName}.",
        };

        var link = CustomerLink.Resolve(visit.Leader.Id, out var failure);
        if (link is null)
        {
            lines.Add($"Leader {visit.Leader.FullName}: no reachable Customer component ({failure}).");
            lines.Add("Nothing can be sold to this group. Send them home and force a new visit.");
            return string.Join("\n", lines);
        }

        lines.Add($"Leader {visit.Leader.FullName}: {(link.IsUnlocked ? "unlocked" : "LOCKED - the game will refuse every offer")}.");
        lines.Add(link.HasOffer
            ? $"Live offer: ${link.OfferPayment:0}, waiting for you to accept or reject it on the phone."
            : "Live offer: none.");
        lines.Add(link.HasContract
            ? $"Accepted contract: {link.ContractTitle} ({link.ContractState}), ${link.ContractPayment:0}."
            : "Accepted contract: none.");
        lines.Add($"Phone: {link.PhoneMessageCount} message(s), {(link.PhoneResponsesActive ? $"{link.PhoneResponseCount} answerable response(s) showing" : "no answerable responses")}.");
        lines.Add($"Delivery point the game would use: {link.DeliveryLocationName}.");
        lines.Add($"Completed handovers with this leader: {link.CompletedDeliveries}.");
        lines.Add($"Offer attempts this visit: {visit.OfferAttempts} of {MaxOfferAttempts}{(visit.OfferSent ? ", one succeeded" : "")}.");
        lines.Add("Next step: " + NextStep(visit, link));

        if (OfferFactory.LastDiagnosis.Length > 0)
            lines.Add("Last offer attempt: " + OfferFactory.LastDiagnosis);

        return string.Join("\n", lines);
    }

    private static string NextStep(Visit visit, CustomerLink link)
    {
        if (link.HasContract)
            return $"go to {link.DeliveryLocationName} with the product and talk to {visit.Leader.FullName} to hand it over.";

        if (link.HasOffer)
            return link.PhoneResponsesActive
                ? "open the phone's Messages app and accept the offer."
                : "the offer is recorded but the phone is showing no buttons. Use \"Clear the group's stuck order\", then \"Make the group offer now\".";

        if (!link.IsUnlocked)
            return "the leader is locked as a customer. Send the group home and bring them back in - arrival unlocks them.";

        return visit.OfferSent
            ? "no offer is outstanding but one was already sent this visit; use \"Make the group offer now\" to send another."
            : $"wait until {GameClock.Format(visit.OrderTime)}, or use \"Make the group offer now\".";
    }

    private string NextArrivalLine()
    {
        var today = GameClock.ElapsedDays;
        var wait = _state.NextVisitDay - today;
        return wait > 0
            ? $"The next group is due on day {_state.NextVisitDay}, {wait} day(s) away."
            : $"The next group is due now (day {_state.NextVisitDay}, today is {today}).";
    }

    /// <summary>
    /// Walks the whole pool rather than just the current leader: a slot that led an earlier visit can
    /// still be holding that visit's offer, and it will silently block the next one it leads.
    /// </summary>
    private static ClearReport ClearPoolOfferState(bool includeContracts)
    {
        var offers = 0;
        var contracts = 0;
        var problems = new List<string>();

        foreach (var slot in VisitorSlot.All)
        {
            var link = CustomerLink.Resolve(slot.Id, out _);
            if (link is null)
                continue;

            if (link.HasOffer)
            {
                if (link.ClearOffer(out var offerFailure))
                    offers++;
                else
                    problems.Add($"{slot.FullName}'s offer ({offerFailure})");
            }

            if (!includeContracts || !link.HasContract)
                continue;

            if (link.ExpireContract(out var contractFailure))
                contracts++;
            else
                problems.Add($"{slot.FullName}'s contract ({contractFailure})");
        }

        if (offers > 0 || contracts > 0)
            VisitorLog.Instance.Msg($"Cleared {offers} stale group offer(s) and {contracts} contract(s) from the visitor pool.");

        return new ClearReport(offers, contracts, problems);
    }

    private readonly struct ClearReport
    {
        private readonly IReadOnlyList<string>? _problems;

        internal ClearReport(int offers, int contracts, IReadOnlyList<string> problems)
        {
            Offers = offers;
            Contracts = contracts;
            _problems = problems;
        }

        internal int Offers { get; }

        internal int Contracts { get; }

        /// <summary>Never null: a <c>default</c> report is the "nothing was attempted" case.</summary>
        internal IReadOnlyList<string> Problems => _problems ?? Array.Empty<string>();
    }

    private void OnPoolSettled()
    {
        // Before anything else, and for every slot rather than only the idle ones: a save reloaded
        // mid-visit skips the parking pass entirely, and an unpinned visitor is one the generic
        // civilian schedule will walk into the next district.
        foreach (var slot in VisitorSlot.All)
        {
            if (!PostWatch.Pin(slot, out var pinFailure))
                VisitorLog.Instance.Debug($"Could not pin slot {slot.Index:00} to its post ({pinFailure}).");
        }

        var pending = _pendingRestore;
        _pendingRestore = null;

        if (pending is not null)
        {
            Restore(pending);
        }
        else
        {
            ParkIdlePool();
            Tick();
        }

        // Only now is the visit either rebuilt or definitively absent, so the watchdog can tell an
        // idle visitor apart from one standing exactly where the group put him.
        PostWatch.Arm();
    }

    /// <summary>
    /// Between visits the pool is invisible and out of the way. Slot 01 is the exception: he was in
    /// the world before the pool existed, he is what the menu's teleport points at, and one visible
    /// stranger is a cost worth paying for a landmark the player can find.
    /// </summary>
    private void ParkIdlePool()
    {
        if (_poolParked || !HostGate.IsAuthority)
            return;

        lock (_gate)
        {
            if (_visit is not null)
                return;
        }

        foreach (var slot in VisitorSlot.All)
        {
            // Every visitor is taken out of the generic civilian routine, scout included. That is
            // the actual fix for a parked visitor turning up in the wrong district: nothing else was
            // stopping the default schedule walking him there.
            if (!PostWatch.Pin(slot, out var pinFailure))
                VisitorLog.Instance.Debug($"Could not pin slot {slot.Index:00} to its post ({pinFailure}).");

            if (slot.IsResidentScout)
            {
                ArrivalStagger.SetVisible(slot, true, out _);
                continue;
            }

            Congregation.Park(slot);
            ArrivalStagger.SetVisible(slot, false, out _);
        }

        _poolParked = true;
    }

    private void Restore(VisitStateData data)
    {
        _state = data;

        if (_state.NextVisitDay <= 0)
            ScheduleNext(from: GameClock.ElapsedDays);

        // A save that recorded a self-disable under an older config re-checks it here, so clearing
        // detection_mode back to auto after an always_on session does not silently stay enabled.
        if (_state.SelfDisabledByDetection && OfficialFeatureDetector.Verdict.IsEnabled)
        {
            var verdict = OfficialFeatureDetector.Evaluate(true, _state.SelfDisableReason);
            OfficialFeatureDetector.Report(verdict);

            if (!verdict.IsEnabled)
                return;

            _state.SelfDisabledByDetection = false;
            _state.SelfDisableReason = string.Empty;
        }

        // The group cannot be rebuilt before the NPCs it is made of exist. S1API creates them during
        // the same load, so a blob that arrives first waits for the pool rather than warning eight
        // times that nobody could be dressed.
        if (_state.Active is not null && !(VisitorRuntime.CustomNpcsReady() && VisitorRuntime.ResolvedCount() > 0))
        {
            _pendingRestore = _state;
            return;
        }

        if (_state.Active is null)
        {
            _poolParked = false;
            ParkIdlePool();
            return;
        }

        var visit = Visit.FromSave(_state.Active);
        if (visit is null)
        {
            VisitorLog.Instance.Warn(
                "The saved group could not be rebuilt (its archetype or members no longer exist); starting clean.");
            _state.Active = null;
            Persist();
            _poolParked = false;
            ParkIdlePool();
            return;
        }

        // A save reloaded past the group's departure never gets an arrival — it gets the unwind, so
        // the world is exactly as it would have been had the player stayed logged in.
        if (GameClock.Now >= visit.DepartureStamp && HostGate.IsAuthority)
        {
            RedressForUnwind(visit);
            End(visit, silent: true, reschedule: true);
            return;
        }

        lock (_gate)
            _visit = visit;

        _poolParked = false;

        // A co-op guest tracks the visit so the menu, the tutorial and the status read correctly,
        // but never re-warps or re-reveals anybody: the host owns the world and the guest receives
        // the group already placed and visible.
        if (HostGate.IsAuthority)
        {
            Dress(visit);
            Congregation.Place(visit);

            foreach (var slot in visit.Members)
                ArrivalStagger.SetVisible(slot, true, out _);
        }

        // Dialogue lives on runtime components, so it does not survive a load and has to be rebuilt
        // on every peer rather than only on the host.
        VisitorDialogue.Attach(visit);
        VisitAnnouncer.RestoreMarker(visit);
        VisitorLog.Instance.Msg(
            $"{visit.Archetype.DisplayName} are still in {WorldGeography.NameOf(visit.Region)} at {visit.DeliveryLocationName}.");

        Tick();
    }

    private void Tick()
    {
        if (!_attached || !OfficialFeatureDetector.Verdict.IsEnabled)
            return;

        // Arrivals never fire mid-sleep: a group revealed while the screen is black is a group the
        // player never saw arrive. The sleep-end event runs this again the moment they wake.
        if (GameClock.SleepInProgress)
            return;

        if (!HostGate.IsAuthority)
            return;

        Visit? visit;
        lock (_gate)
            visit = _visit;

        if (visit is not null)
        {
            if (GameClock.Now >= visit.DepartureStamp)
            {
                End(visit, silent: false, reschedule: true);
                return;
            }

            if (!visit.OfferSent && visit.OfferAttempts < MaxOfferAttempts && GameClock.Now >= visit.OrderStamp)
            {
                visit.OfferAttempts++;

                var result = OfferFactory.Send(visit, out var failure);
                if (result.Sent)
                {
                    visit.OfferSent = true;
                }
                else
                {
                    var exhausted = visit.OfferAttempts >= MaxOfferAttempts;
                    VisitorLog.Instance.Warn(
                        $"{visit.Archetype.DisplayName} could not place their bulk order (attempt {visit.OfferAttempts} " +
                        $"of {MaxOfferAttempts}): {failure}" +
                        (exhausted
                            ? " No further attempts this visit — use \"Make the group offer now\" on the Expansions screen once the cause is fixed."
                            : " Retrying on the next hour."));
                }

                Persist();
            }

            return;
        }

        var today = GameClock.ElapsedDays;
        if (today < _state.NextVisitDay)
            return;

        if (GameClock.MinutesOfDay(GameClock.CurrentTime) < GameClock.MinutesOfDay(CustomerSettings.ArrivalTime))
            return;

        var archetype = PickArchetype();
        if (archetype is null)
        {
            // Nothing to send. Try again tomorrow rather than every hour for the rest of the save.
            ScheduleNext(from: today);
            Persist();
            return;
        }

        if (!Begin(archetype, out var reason))
        {
            VisitorLog.Instance.Debug($"No group arrived today: {reason}");
            ScheduleNext(from: today);
            Persist();
        }
    }

    private bool Begin(Archetype archetype, out string failure)
    {
        if (!VisitorRuntime.CustomNpcsReady())
        {
            failure = "the visitor NPCs are not in the world yet";
            return false;
        }

        var region = PickRegion(archetype);
        if (region is null)
        {
            failure = $"no unlocked region suits {archetype.ShortName}";
            return false;
        }

        var locations = WorldGeography.LocationsIn(region.Value);
        if (locations.Count == 0)
        {
            failure = $"{WorldGeography.NameOf(region.Value)} has no delivery location to meet at";
            return false;
        }

        var rng = new LookRandom(archetype.Id, GameClock.ElapsedDays);
        var location = locations[rng.Next(locations.Count)];
        var standPoint = WorldGeography.StandPointOf(location);
        if (standPoint is null)
        {
            failure = $"'{location.Name}' has no usable stand point";
            return false;
        }

        var members = PickMembers(archetype, ref rng);
        if (members.Count == 0)
        {
            failure = DescribeEmptyPool();
            return false;
        }

        // Incremented per visit and persisted, so a second visit on the same in-game day still
        // brings visibly different people, and a reload rebuilds exactly the ones already in town.
        _state.VisitSerial++;

        var visit = Visit.Begin(
            archetype,
            region.Value,
            SafeGuid(location),
            location.Name ?? "an agreed spot",
            standPoint.Value,
            members,
            members[0],
            GameClock.ElapsedDays,
            CustomerSettings.ArrivalTime,
            CustomerSettings.DepartureTime,
            AppearanceSeed(archetype, GameClock.ElapsedDays, _state.VisitSerial));

        lock (_gate)
            _visit = visit;

        _poolParked = false;

        Dress(visit);

        // Warp first, while everyone is still hidden, so navmesh work never lands on the same frame
        // as avatar compositing.
        Congregation.Place(visit);
        _stagger.Reveal(visit.Members);

        // Anything the pool is still holding belongs to a group that has already gone home, and the
        // shipped OfferContract drops a new offer without a word while either is set. Wiping the
        // slate here is what stops a leader wedging on "already has a pending order" forever.
        ClearPoolOfferState(includeContracts: true);

        VisitorDialogue.Attach(visit);
        VisitAnnouncer.AnnounceArrival(visit);
        Persist();

        VisitorLog.Instance.Msg(
            $"{archetype.DisplayName} arrived in {WorldGeography.NameOf(region.Value)} at {visit.DeliveryLocationName} " +
            $"with {members.Count} member(s); they order at {GameClock.Format(visit.OrderTime)} and leave at {GameClock.Format(visit.DepartureTime)}.");

        failure = string.Empty;
        return true;
    }

    private void Dress(Visit visit)
    {
        var budget = EstimateBudget(visit.Archetype);

        foreach (var slot in visit.Members)
        {
            // Seeded on the pool slot and the visit, not on the member's position in the group, so a
            // group of four and a group of six put the same face on the same slot.
            if (!VisitorDresser.Apply(slot, visit.Archetype, visit.AppearanceSeed, out var dressFailure))
            {
                VisitorLog.Instance.Warn(
                    $"{slot.FullName} could not be dressed as {visit.Archetype.ShortName} ({dressFailure}); they join in civilian clothes.");
            }

            if (!CustomerTuner.Apply(slot, visit.Archetype, slot.Index == visit.LeaderSlot, budget, out var tuneFailure))
            {
                VisitorLog.Instance.Warn(
                    $"{slot.FullName} could not be tuned as a {visit.Archetype.ShortName} customer ({tuneFailure}); " +
                    "they will be in the group but may not buy.");
            }
        }
    }

    /// <summary>Re-applies enough state that the unwind has something to undo after a cold load.</summary>
    private void RedressForUnwind(Visit visit)
    {
        lock (_gate)
            _visit = visit;
    }

    private void End(Visit visit, bool silent, bool reschedule)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_visit, visit))
                return;

            _visit = null;
        }

        _stagger.Clear();
        VisitorDialogue.Detach();

        var authoritative = HostGate.IsAuthority;

        // The group is walking out of town, so neither an unanswered offer nor an accepted contract
        // can still be honoured — the customer is about to be hidden and parked. Leaving either
        // behind is what wedged the leader into "already has a pending order" on every later visit.
        var cleared = authoritative ? ClearPoolOfferState(includeContracts: true) : default;

        foreach (var slot in visit.Members)
        {
            // Undressing and untuning are local-only and a no-op on a peer that never applied them,
            // so they run everywhere. Warping and visibility are server-authoritative and do not.
            CustomerTuner.Restore(slot, out var tuneFailure);
            if (tuneFailure.Length > 0)
                VisitorLog.Instance.Debug($"Restoring customer data for slot {slot.Index:00}: {tuneFailure}");

            VisitorDresser.Restore(slot, out var dressFailure);
            if (dressFailure.Length > 0)
                VisitorLog.Instance.Debug($"Restoring the neutral look for slot {slot.Index:00}: {dressFailure}");

            if (!authoritative)
                continue;

            Congregation.Park(slot);
            ArrivalStagger.SetVisible(slot, slot.IsResidentScout, out _);
        }

        VisitAnnouncer.AnnounceDeparture(visit, silent);

        _state.LastArchetypeId = visit.Archetype.Id;
        _state.Active = null;

        if (reschedule)
            ScheduleNext(from: GameClock.ElapsedDays);

        Persist();
        _poolParked = true;

        if (!silent)
        {
            var abandoned = cleared.Offers + cleared.Contracts > 0
                ? $" They took {cleared.Offers} unanswered offer(s) and {cleared.Contracts} undelivered contract(s) with them."
                : string.Empty;

            VisitorLog.Instance.Msg(
                $"{visit.Archetype.DisplayName} left town.{abandoned} Next group due on day {_state.NextVisitDay}.");
        }
    }

    /// <summary>Weighted by archetype, never the same group twice running while an alternative exists.</summary>
    private Archetype? PickArchetype()
    {
        var enabled = ArchetypeCatalog.Enabled();
        if (enabled.Count == 0)
            return null;

        var eligible = enabled.Count > 1
            ? enabled.Where(a => !string.Equals(a.Id, _state.LastArchetypeId, StringComparison.Ordinal)).ToList()
            : enabled.ToList();

        if (eligible.Count == 0)
            eligible = enabled.ToList();

        var rng = new LookRandom("archetype", GameClock.ElapsedDays);
        return eligible[rng.Next(eligible.Count)];
    }

    private Region? PickRegion(Archetype archetype)
    {
        var unlocked = WorldGeography.UnlockedRegions();
        var pool = new List<Region>();

        foreach (var (region, weight) in archetype.RegionWeights)
        {
            if (!unlocked.Contains(region))
                continue;

            for (var i = 0; i < weight; i++)
                pool.Add(region);
        }

        // The archetype's own districts are all still locked, so they go wherever is open rather
        // than the visit silently never happening.
        if (pool.Count == 0)
            pool.AddRange(unlocked);

        if (pool.Count == 0)
            return null;

        var rng = new LookRandom(archetype.Id + ":region", GameClock.ElapsedDays);
        return pool[rng.Next(pool.Count)];
    }

    private static List<int> PickMembers(Archetype archetype, ref LookRandom rng)
    {
        var jitter = CustomerSettings.GroupSizeJitter;
        var size = archetype.DefaultMemberCount + (jitter > 0 ? rng.Next((jitter * 2) + 1) - jitter : 0);
        size = Math.Clamp(size, 1, Math.Min(CustomerSettings.MaxGroupSize, VisitorSlot.Count));

        var members = new List<int>(size);
        foreach (var slot in VisitorSlot.All)
        {
            if (members.Count >= size)
                break;

            if (VisitorRuntime.Resolve(slot) is not null)
                members.Add(slot.Index);
        }

        return members;
    }

    /// <summary>
    /// One-line explanation of why a visit cannot start — names every missing slot and the last
    /// rejection reason S1API / SpawnGraphFix recorded for it.
    /// </summary>
    private static string DescribeEmptyPool()
    {
        var missing = new List<string>();
        foreach (var slot in VisitorSlot.All)
        {
            var status = VisitorRuntime.StatusOf(slot);
            if (status.WrapperResolved)
                continue;

            missing.Add($"{slot.Index:00}/{slot.FullName}: {status.Failure}");
        }

        if (missing.Count == 0)
            return "no visitor slots are available (pool looked empty but every slot reports resolved — retry)";

        return
            $"no visitor slots are available ({VisitorRuntime.ResolvedCount()}/{VisitorSlot.Count} in world). " +
            string.Join("; ", missing);
    }

    private static float EstimateBudget(Archetype archetype) =>
        archetype.QuantityMax * 200f * archetype.PriceMultiplier;

    /// <summary>
    /// The number every member's appearance is derived from. Deterministic in its inputs so two
    /// co-op peers compute the same one, and never zero, because zero means "no seed recorded".
    /// </summary>
    private static int AppearanceSeed(Archetype archetype, int day, int serial)
    {
        var rng = new LookRandom(archetype.Id + ":appearance", day, serial);
        var seed = rng.Next(int.MaxValue);
        return seed == 0 ? 1 : seed;
    }

    private static string SafeGuid(DeliveryLocation location)
    {
        try
        {
            return location.GUID ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private void ScheduleNext(int from)
    {
        var rng = new LookRandom("cadence", from);
        var span = CustomerSettings.IntervalDaysMin +
                   rng.Next((CustomerSettings.IntervalDaysMax - CustomerSettings.IntervalDaysMin) + 1);

        _state.NextVisitDay = from + Math.Max(1, span);
    }

    private void Persist()
    {
        var current = SpecialCustomerState.Current;
        if (current is null)
            return;

        try
        {
            Visit? visit;
            lock (_gate)
                visit = _visit;

            current.State.SchemaVersion = VisitStateData.CurrentSchemaVersion;
            current.State.NextVisitDay = _state.NextVisitDay;
            current.State.LastArchetypeId = _state.LastArchetypeId;
            current.State.VisitSerial = _state.VisitSerial;
            current.State.SelfDisabledByDetection = _state.SelfDisabledByDetection;
            current.State.SelfDisableReason = _state.SelfDisableReason;
            current.State.Active = visit?.ToSave();

            _state = current.State;
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Debug($"Could not write the group state ({Describe.Of(ex)}); it will be rebuilt next load.");
        }
    }

    /// <summary>Records a self-disable in the save so it stays off for that save until overridden.</summary>
    internal void RecordSelfDisable(string reason)
    {
        _state.SelfDisabledByDetection = true;
        _state.SelfDisableReason = reason;
        Persist();
    }

    private void Shutdown()
    {
        Detach();
        Unwind(silent: true);
    }

    private void Detach()
    {
        if (_hourHandler is not null)
        {
            Unsubscribe(ref TimeManager.OnHourPass, _hourHandler);
            _hourHandler = null;
        }

        if (_dayHandler is not null)
        {
            Unsubscribe(ref TimeManager.OnDayPass, _dayHandler);
            _dayHandler = null;
        }

        if (_sleepHandler is not null)
        {
            Unsubscribe(ref TimeManager.OnSleepEnd, _sleepHandler);
            _sleepHandler = null;
        }

        if (_stateHandler is not null)
        {
            SpecialCustomerState.Loaded -= _stateHandler;
            _stateHandler = null;
        }

        if (_poolHandler is not null)
        {
            VisitorRuntime.PoolSettled -= _poolHandler;
            _poolHandler = null;
        }

        _attached = false;
    }

    /// <summary>
    /// S1API's time hooks are plain static <b>fields</b>, not events, so removing the last handler
    /// leaves a null in a slot the nullable annotations say cannot hold one. Going through
    /// <c>Delegate.Remove</c> says that out loud instead of suppressing it.
    /// </summary>
    private static void Unsubscribe<T>(ref T field, T handler)
        where T : Delegate =>
        field = (Delegate.Remove(field, handler) as T)!;

    /// <summary>
    /// One failing hook must never take the module down: Core counts frame-hook exceptions and
    /// auto-disables at ten, and losing the whole feature because a delivery location moved would be
    /// a bad trade.
    /// </summary>
    private void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            VisitorLog.Instance.Error($"Special Customers failed during {what}; the module carries on.", ex);
        }
    }
}
