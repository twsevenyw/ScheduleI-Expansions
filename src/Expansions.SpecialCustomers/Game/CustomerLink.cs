using Expansions.Core.Diagnostics;

namespace Expansions.SpecialCustomers.Game;

/// <summary>
/// A resolved handle on one visitor's live <c>Customer</c> behaviour: everything the sale loop has to
/// read to know where it is, and the two writes it needs to unstick itself.
/// <para>
/// The shipped <c>Customer.OfferContract</c> returns <c>void</c> and silently walks away when the
/// customer is already holding a contract or an unanswered offer — S1API's wrapper reports
/// <c>true</c> in exactly that case, because all it can see is that the call did not throw. So the
/// only honest way to know an offer landed is to read <c>OfferedContractInfo</c> back afterwards,
/// which is what this type exists for.
/// </para>
/// </summary>
internal sealed class CustomerLink
{
    private readonly GameNpc _npc;
    private readonly object _customer;

    private CustomerLink(GameNpc npc, object customer)
    {
        _npc = npc;
        _customer = customer;
    }

    internal string Id => _npc.Id;

    internal GameNpc Npc => _npc;

    internal static CustomerLink? Resolve(string npcId, out string failure)
    {
        var npc = GameNpc.Resolve(npcId, out failure);
        return npc is null ? null : Resolve(npc, out failure);
    }

    internal static CustomerLink? Resolve(GameNpc npc, out string failure)
    {
        var customer = npc.Customer(out failure);
        if (customer is null)
            return null;

        var typed = InteropCast.As(customer, GameTypes.Customer) ?? customer;
        return new CustomerLink(npc, typed);
    }

    /// <summary>A contract the player has already accepted and not yet delivered on.</summary>
    internal object? Contract =>
        GameReflection.TryRead(_customer, "CurrentContract", out var value, out _) && GameReflection.IsPresent(value)
            ? value
            : null;

    internal bool HasContract => Contract is not null;

    /// <summary>An offer that has been made and neither accepted, rejected nor expired.</summary>
    internal object? Offer =>
        GameReflection.TryRead(_customer, "OfferedContractInfo", out var value, out _) && GameReflection.IsPresent(value)
            ? value
            : null;

    internal bool HasOffer => Offer is not null;

    internal float OfferPayment => ReadFloat(Offer, "Payment");

    internal float ContractPayment => ReadFloat(Contract, "Payment");

    internal string ContractTitle => Contract is { } contract && GameReflection.TryRead(contract, "Title", out var value, out _)
        ? GameReflection.Format(value)
        : string.Empty;

    internal string ContractState => Contract is { } contract && GameReflection.TryRead(contract, "State", out var value, out _)
        ? GameReflection.Format(value)
        : string.Empty;

    /// <summary>Absolute game minutes at which the current offer was made, or -1 when there is none.</summary>
    internal long OfferedAtMinutes
    {
        get
        {
            if (!GameReflection.TryRead(_customer, "OfferedContractTime", out var stamp, out _) || stamp is null)
                return -1;

            var days = ReadInt(stamp, "elapsedDays");
            var time = ReadInt(stamp, "time");
            return ((long)days * 1440L) + ((time / 100) * 60) + (time % 100);
        }
    }

    internal int OfferedDeals => ReadInt(_customer, "OfferedDeals");

    internal int CompletedDeliveries => ReadInt(_customer, "CompletedDeliveries");

    internal int MinutesSinceLastOffer => ReadInt(_customer, "TimeSinceLastDealOffered");

    internal int MinutesSinceInstantDeal => ReadInt(_customer, "TimeSinceInstantDealOffered");

    internal bool PendingInstantDeal =>
        GameReflection.TryRead(_customer, "pendingInstantDeal", out var value, out _) && value is true;

    internal bool IsAwaitingDelivery =>
        GameReflection.TryRead(_customer, "IsAwaitingDelivery", out var value, out _) && value is true;

    internal bool IsUnlocked => _npc.IsUnlocked;

    /// <summary>Whether the shipped generator currently believes this customer would place an order.</summary>
    internal bool ShouldTryGenerateDeal(out string failure) =>
        GameReflection.TryInvoke(
            _customer.GetType(), _customer, "ShouldTryGenerateDeal", Array.Empty<object?>(), out var value, out failure) &&
        value is true;

    /// <summary>
    /// Where the shipped code would send this customer to meet the player when no contract pins a
    /// spot. <c>GetDeliveryLocation</c> can pick a random unscheduled site, so it is not the same
    /// thing as the location baked into a live offer or accepted contract.
    /// </summary>
    internal string DeliveryLocationName
    {
        get
        {
            if (!GameReflection.TryInvoke(
                    _customer.GetType(), _customer, "GetDeliveryLocation", Array.Empty<object?>(), out var location, out var failure))
            {
                return $"unreadable ({failure})";
            }

            if (!GameReflection.IsPresent(location))
                return "none";

            // The game-side type calls it LocationName; only S1API's wrapper exposes it as Name.
            return GameReflection.TryRead(location, "LocationName", out var name, out _)
                ? GameReflection.Format(name)
                : GameReflection.Format(location);
        }
    }

    /// <summary>
    /// Delivery spot on the outstanding offer or accepted contract — what the handover will actually
    /// use. Falls back to <see cref="DeliveryLocationName"/> only when neither is set.
    /// </summary>
    internal string ActiveDeliveryLocationName
    {
        get
        {
            var fromOffer = LocationNameOf(Offer);
            if (fromOffer.Length > 0)
                return fromOffer;

            var fromContract = LocationNameOf(Contract);
            if (fromContract.Length > 0)
                return fromContract;

            return DeliveryLocationName;
        }
    }

    private static string LocationNameOf(object? contractOrInfo)
    {
        if (contractOrInfo is null)
            return string.Empty;

        if (GameReflection.TryRead(contractOrInfo, "DeliveryLocation", out var location, out _) &&
            GameReflection.IsPresent(location))
        {
            if (GameReflection.TryRead(location, "LocationName", out var name, out _))
                return GameReflection.Format(name);

            return GameReflection.Format(location);
        }

        if (GameReflection.TryRead(contractOrInfo, "DeliveryLocationGUID", out var guid, out _) ||
            GameReflection.TryRead(contractOrInfo, "DeliveryLocationGuid", out guid, out _))
        {
            var text = GameReflection.Format(guid);
            if (text.Length > 0 && !string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
                return text;
        }

        return string.Empty;
    }

    /// <summary>True when the phone conversation is showing answerable responses right now.</summary>
    internal bool PhoneResponsesActive =>
        _npc.Conversation is { } conversation &&
        GameReflection.TryRead(conversation, "AreResponsesActive", out var value, out _) &&
        value is true;

    internal int PhoneResponseCount =>
        _npc.Conversation is { } conversation &&
        GameReflection.TryRead(conversation, "currentResponses", out var responses, out _) &&
        responses is not null
            ? GameReflection.Enumerate(responses).Count
            : 0;

    internal int PhoneMessageCount =>
        _npc.Conversation is { } conversation &&
        GameReflection.TryRead(conversation, "messageHistory", out var history, out _) &&
        history is not null
            ? GameReflection.Enumerate(history).Count
            : 0;

    /// <summary>
    /// Drops an unanswered offer so a new one can be made.
    /// <para>
    /// The shipped <c>ExpireOffer</c> is tried first because it is what the game's own two-hour
    /// timeout calls: it clears the phone responses as well as the field. Only if the field survives
    /// that is the backing field nulled directly, which handles an offer left behind by a departure
    /// the expiry timer never got to see.
    /// </para>
    /// </summary>
    internal bool ClearOffer(out string failure)
    {
        failure = string.Empty;

        if (!HasOffer)
            return true;

        if (!GameReflection.TryInvoke(
                _customer.GetType(), _customer, "ExpireOffer", Array.Empty<object?>(), out _, out var expireFailure))
        {
            failure = expireFailure;
        }

        if (!HasOffer)
            return true;

        // The backing field first, then the property: the two are separate members on the interop
        // side, and only clearing what the game reads back counts as cleared.
        GameReflection.TryWrite(_customer, "offeredContractInfo", null, out var fieldFailure);

        if (HasOffer && !GameReflection.TryWrite(_customer, "OfferedContractInfo", null, out var propertyFailure))
        {
            failure = Join(failure, fieldFailure, propertyFailure);
            return false;
        }

        if (HasOffer)
        {
            failure = Join(failure, "the offer accepted a null but still reads back as set");
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static string Join(params string[] parts) =>
        string.Join("; ", parts.Where(static part => part.Length > 0));

    /// <summary>
    /// Expires a contract the player accepted but will never be able to deliver, through the game's
    /// own quest expiry so the journal entry, the map marker and the customer's own bookkeeping all
    /// unwind together.
    /// </summary>
    internal bool ExpireContract(out string failure)
    {
        var contract = Contract;
        if (contract is null)
        {
            failure = string.Empty;
            return true;
        }

        if (!GameReflection.TryInvoke(
                contract.GetType(), contract, "Expire", new object?[] { true }, out _, out failure))
        {
            return false;
        }

        if (HasContract)
        {
            failure = "the contract was told to expire but is still attached to the customer";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// Re-runs the shipped dialogue setup, which is what creates the customer's own "deal", "sample"
    /// and "complete contract" choices. It runs during <c>Customer.Start</c> against whatever the
    /// prefab's <c>CustomerData</c> said at the time, so a visitor whose data is written later has to
    /// ask for it again or the interaction menu stays empty.
    /// </summary>
    internal bool SetUpDialogue(out string failure)
    {
        var customerType = GameReflection.FindType(GameTypes.Customer);
        if (customerType is null)
        {
            failure = $"'{GameTypes.Customer}' not found";
            return false;
        }

        var typed = InteropCast.As(_customer, customerType) ?? _customer;
        return GameReflection.TryInvoke(
            customerType, typed, "SetUpDialogue", Array.Empty<object?>(), out _, out failure);
    }

    /// <summary>
    /// Whether the shipped street-deal request took, judged from the two pieces of state it moves.
    /// The call itself returns void and the game throttles it, so this is the only honest answer.
    /// </summary>
    internal bool WalkUpDealOpened(int minutesSinceBefore) =>
        PendingInstantDeal || MinutesSinceInstantDeal < minutesSinceBefore;

    private static float ReadFloat(object? instance, string member) =>
        instance is not null && GameReflection.TryRead(instance, member, out var value, out _) && value is float number
            ? number
            : 0f;

    private static int ReadInt(object? instance, string member) =>
        instance is not null && GameReflection.TryRead(instance, member, out var value, out _) && value is int number
            ? number
            : 0;
}
