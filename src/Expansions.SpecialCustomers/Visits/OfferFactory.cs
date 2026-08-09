using Expansions.Core.Diagnostics;
using Expansions.SpecialCustomers.Archetypes;
using Expansions.SpecialCustomers.Configuration;
using Expansions.SpecialCustomers.Game;
using Expansions.SpecialCustomers.Visitors;
using S1API.Economy;
using S1API.Leveling;
using S1API.Products;
using UnityEngine;

namespace Expansions.SpecialCustomers.Visits;

/// <summary>
/// Authors the group's bulk offer — and nothing else.
/// <para>
/// The offer is the only thing this mod writes. Everything after it is shipped code: the phone
/// message, the accept/reject/counter-offer flow, the deal-window picker, the customer walking to
/// the delivery location, the handover screen, the quality match, the payment, the XP and the
/// receipt in the analytics app. That is why the core loop needs no Harmony patches, and why the
/// money is real.
/// </para>
/// <para>
/// The below-market margin is expressed as a smaller <c>Payment</c>, never as a patched formula. The
/// player experiences it as "they pay less per unit, but they will take everything".
/// </para>
/// </summary>
internal static class OfferFactory
{
    /// <summary>
    /// Fallback when <c>Customer.MaxOrderQuantityPerProduct</c> cannot be read. Live probes on
    /// 0.4.6 report that static as <c>1000</c> — the same ceiling the deal formula clamps to.
    /// </summary>
    private const int FallbackMaxQuantity = 1000;

    /// <summary>The shipped rounding rule: orders of 14 or more snap to a multiple of five.</summary>
    private const int RoundingThreshold = 14;

    /// <summary>
    /// Stand-in for the shipped appeal curve, which multiplies market value by roughly
    /// (enjoyment + 0.95). Higher standards demand better product and pay for it.
    /// </summary>
    private static float QualityFactor(Quality quality) => quality switch
    {
        Quality.Trash => 1.00f,
        Quality.Poor => 1.15f,
        Quality.Standard => 1.30f,
        Quality.Premium => 1.45f,
        _ => 1.60f,
    };

    internal static string LastFailure { get; private set; } = string.Empty;

    /// <summary>What the last attempt actually observed, in the owner's language rather than the log's.</summary>
    internal static string LastDiagnosis { get; private set; } = string.Empty;

    internal static Result Send(Visit visit, out string failure)
    {
        LastFailure = string.Empty;
        LastDiagnosis = string.Empty;

        var leader = VisitorRuntime.Resolve(visit.Leader);
        if (leader is null)
        {
            failure = LastFailure = "the group leader is not in the world";
            return Result.None;
        }

        var link = CustomerLink.Resolve(visit.Leader.Id, out var linkFailure);
        if (link is null)
        {
            failure = LastFailure = $"the group leader has no reachable Customer component ({linkFailure})";
            return Result.None;
        }

        if (!Prepare(link, out failure))
        {
            LastFailure = failure;
            return Result.None;
        }

        var lines = BuildLines(visit, out failure);
        if (lines.Count == 0)
        {
            LastFailure = failure;
            return Result.None;
        }

        var payment = lines.Sum(line => line.Payment);
        payment = Mathf.Max(1f, Mathf.Round(payment / 5f) * 5f);

        var builder = new ContractInfoBuilder()
            .WithPayment(payment)
            .WithDeliveryWindow(visit.OrderTime, GameClock.AddHours(visit.OrderTime, 4))
            .WithExpiration(true)
            .ExpiresAfter(CustomerSettings.OfferExpiryMinutes);

        foreach (var line in lines)
            builder.AddProduct(line.Product, line.Quantity, line.MinQuality);

        if (visit.DeliveryLocationGuid.Length > 0)
            builder.WithDeliveryLocationByGuid(visit.DeliveryLocationGuid);

        var dealsBefore = link.OfferedDeals;

        bool called;
        try
        {
            called = leader.Customer.OfferContract(builder.Build());
        }
        catch (Exception ex)
        {
            failure = LastFailure = Describe.Of(ex);
            return Result.None;
        }

        if (!called)
        {
            failure = LastFailure =
                "S1API would not hand the contract to the game — the leader's Customer component is missing or the offer was malformed";
            return Result.None;
        }

        // The shipped OfferContract returns void and gives up quietly when the customer is already
        // holding something, so "it did not throw" is not evidence. Reading the offer back is.
        if (!link.HasOffer)
        {
            failure = LastFailure = Diagnose(link);
            LastDiagnosis = failure;
            return Result.None;
        }

        var products = Name(lines);
        var total = lines.Sum(line => line.Quantity);
        // Log only — OfferContract already put the answerable offer on the phone. Sending flavour
        // text here was wiping Accept/Reject/Counter (see VisitAnnouncer.AnnounceOffer).
        VisitAnnouncer.AnnounceOffer(visit, total, products, payment);

        LastDiagnosis =
            $"Offer recorded on {visit.Leader.FullName}: ${link.OfferPayment:0} for {total} x {products}, " +
            $"offer #{link.OfferedDeals} (was {dealsBefore}), delivery at {link.ActiveDeliveryLocationName}.";

        failure = string.Empty;
        return new Result(true, total, payment, lines[0].MinQuality, products);
    }

    /// <summary>
    /// Clears whatever would make the shipped code walk away, and refuses when it cannot.
    /// <para>
    /// This is the fix for a leader who is permanently "already holding an order": an offer that
    /// nobody answered before the group left survives on the <c>Customer</c> for the rest of the
    /// save, and every later offer is dropped on the floor without a word.
    /// </para>
    /// </summary>
    private static bool Prepare(CustomerLink link, out string failure)
    {
        if (link.HasContract)
        {
            failure =
                $"{link.Id} is still holding an accepted contract ({link.ContractState}, ${link.ContractPayment:0}). " +
                "Deliver it, or use \"Clear the group's stuck order\" on the Expansions screen.";
            return false;
        }

        if (!link.IsUnlocked)
        {
            failure = "the leader is not unlocked as a customer, so the game will not let them offer anything";
            return false;
        }

        if (link.HasOffer && !link.ClearOffer(out var clearFailure))
        {
            failure = $"a previous unanswered offer could not be cleared ({clearFailure}), so a new one cannot be made";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>Names the specific reason the shipped code dropped an offer that looked like it worked.</summary>
    private static string Diagnose(CustomerLink link)
    {
        if (link.HasContract)
            return "the game turned the offer into a contract the same frame, which means an older one was still live";

        if (!link.IsUnlocked)
            return "the leader is locked as a customer, so the game discarded the offer";

        return
            "the game accepted the call but recorded no offer. The usual cause is a delivery location it could not " +
            "resolve, or a product list it clamped to nothing. Run 'expprobe sc' and read the Sale loop section.";
    }

    /// <summary>"OG Kush" or "OG Kush and Blue Mist" — what the leader's text actually says.</summary>
    private static string Name(IReadOnlyList<Line> lines)
    {
        var names = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            try
            {
                names.Add(string.IsNullOrWhiteSpace(line.Product.Name) ? "product" : line.Product.Name);
            }
            catch
            {
                names.Add("product");
            }
        }

        return names.Count switch
        {
            0 => "product",
            1 => names[0],
            _ => string.Join(" and ", names),
        };
    }

    /// <summary>
    /// One line per drug type the group cares about that is also on the allow-list. Hippies are the
    /// only archetype that normally produces two, which is what makes their order read as mixed.
    /// </summary>
    private static List<Line> BuildLines(Visit visit, out string failure)
    {
        var lines = new List<Line>(2);
        var drugs = visit.Archetype.EffectiveDrugs();
        var catalogue = Candidates(out failure);

        if (catalogue.Count == 0)
        {
            failure = failure.Length > 0
                ? failure
                : "you have not listed any product for sale, so there is nothing for the group to buy";
            return lines;
        }

        var quality = visit.Archetype.MinimumQuality;
        var rng = new LookRandom(visit.Archetype.Id, visit.ArrivalDay, visit.AppearanceSeed);

        foreach (var drug in drugs)
        {
            var product = Best(catalogue, drug);
            if (product is null)
                continue;

            var quantity = Quantity(visit, ref rng);
            var payment = Payment(product, quantity, quality, visit.Archetype);
            lines.Add(new Line(product, quantity, quality, payment));

            if (lines.Count >= 2)
                break;
        }

        // Nothing the group prefers is available. They still travelled, so they take the most
        // valuable thing they are allowed to ask for rather than the visit being a no-op. The
        // fallback stays inside the allow-list: per-archetype taste is a preference within it, never
        // a licence to step outside it.
        if (lines.Count == 0)
        {
            var fallback = MostValuable(catalogue);
            if (fallback is null)
            {
                failure = "no product in the allow-list could be read well enough to put on a contract";
                return lines;
            }

            var quantity = Quantity(visit, ref rng);
            lines.Add(new Line(fallback, quantity, quality, Payment(fallback, quantity, quality, visit.Archetype)));
        }

        // Two lines split the band rather than doubling it: a mixed order is a different shape, not
        // twice the size.
        if (lines.Count == 2)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                var halved = Math.Max(1, Round(lines[i].Quantity * 0.6f));
                lines[i] = new Line(
                    lines[i].Product,
                    halved,
                    lines[i].MinQuality,
                    Payment(lines[i].Product, halved, lines[i].MinQuality, visit.Archetype));
            }
        }

        failure = string.Empty;
        return lines;
    }

    /// <summary>
    /// What the group is allowed to ask for, in preference order.
    /// <para>
    /// With an allow-list configured this is exactly that list and nothing else — an entry the
    /// player has already discovered wins over one they have not, but a product outside the list is
    /// never a candidate, however much the archetype would like it. With the list cleared it falls
    /// back to whatever is listed for sale, then to whatever has been discovered, so an early-game
    /// group is a prompt to start selling rather than silence.
    /// </para>
    /// </summary>
    private static IReadOnlyList<ProductDefinition> Candidates(out string failure)
    {
        failure = string.Empty;

        var allowed = ProductAllowList.Current();
        if (allowed.IsRestricted)
        {
            if (allowed.Matched.Count == 0)
            {
                failure = allowed.BlockingReason;
                return Array.Empty<ProductDefinition>();
            }

            var available = allowed.AvailableProducts;
            return available.Count > 0 ? available : allowed.Products;
        }

        try
        {
            var listed = ProductManager.ListedProducts;
            if (listed is { Length: > 0 })
                return listed.Where(p => p is not null).ToArray();

            var discovered = ProductManager.DiscoveredProducts;
            if (discovered is { Length: > 0 })
                return discovered.Where(p => p is not null).ToArray();
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
        }

        return Array.Empty<ProductDefinition>();
    }

    /// <summary>Highest market value in the candidate set, for the "nothing they prefer" fallback.</summary>
    private static ProductDefinition? MostValuable(IReadOnlyList<ProductDefinition> catalogue)
    {
        ProductDefinition? best = null;
        var bestValue = float.MinValue;

        foreach (var product in catalogue)
        {
            try
            {
                var value = product.MarketValue;
                if (value <= bestValue && best is not null)
                    continue;

                best = product;
                bestValue = value;
            }
            catch
            {
                // A definition that cannot be read is one the group cannot ask for.
            }
        }

        return best;
    }

    /// <summary>Highest market value of the requested drug type, so the group asks for the good stuff.</summary>
    private static ProductDefinition? Best(IReadOnlyList<ProductDefinition> catalogue, DrugType drug)
    {
        ProductDefinition? best = null;
        var bestValue = float.MinValue;

        foreach (var product in catalogue)
        {
            try
            {
                if (product.PrimaryDrugType != drug && !product.DrugTypeValues.Contains(drug))
                    continue;

                var value = product.MarketValue;
                if (value <= bestValue)
                    continue;

                best = product;
                bestValue = value;
            }
            catch
            {
                // A definition that cannot be read is one the group cannot ask for.
            }
        }

        return best;
    }

    private static int Quantity(Visit visit, ref LookRandom rng)
    {
        var archetype = visit.Archetype;
        var ceiling = HardQuantityCeiling();
        var low = Math.Clamp(Math.Max(CustomerSettings.QuantityMin, archetype.QuantityMin), 1, CustomerSettings.QuantityMax);
        var high = Math.Clamp(Math.Min(CustomerSettings.QuantityMax, archetype.QuantityMax), low, ceiling);

        // Rides the shipped rank curve rather than inventing one, so the mod self-calibrates to a
        // future balance patch: early game sits near the bottom of the band, endgame near the top.
        var rank = Mathf.Clamp01(RankScale() / 10f);
        var position = Mathf.Clamp01(0.35f + (0.65f * rank) + rng.Range(-0.15f, 0.15f));

        // Bigger crews buy more: a full six-person caravan is not the same order as three.
        var groupScale = GroupQuantityScale(visit);
        var quantity = Round(Mathf.Lerp(low, high, position) * CustomerSettings.QuantityMultiplier * groupScale);
        quantity = Math.Clamp(quantity, 1, ceiling);

        if (quantity >= RoundingThreshold)
            quantity = Math.Max(5, Round(quantity / 5f) * 5);

        return quantity;
    }

    /// <summary>
    /// Live <c>Customer.MaxOrderQuantityPerProduct</c> (1000 on 0.4.6). Hand-built
    /// <c>ContractInfo</c> lines are not re-clamped by <c>TryGenerateContract</c>, but the handover
    /// path and member auto-deals both honour this static, so the bulk author stays inside it.
    /// </summary>
    internal static int HardQuantityCeiling()
    {
        try
        {
            if (GameReflection.TryReadStatic(GameTypes.Customer, "MaxOrderQuantityPerProduct", out var value, out _) &&
                value is int ceiling &&
                ceiling > 0)
            {
                return ceiling;
            }
        }
        catch
        {
            // Fall through to the probed default.
        }

        return FallbackMaxQuantity;
    }

    /// <summary>
    /// Member street deals target this many units (before the game's own spend÷price maths and the
    /// hard ceiling). Roughly 40% of the bulk floor, so a walk-up is still a special-customer sale
    /// without matching the leader's whole truck.
    /// </summary>
    internal static int MemberTargetQuantity(Visit visit)
    {
        var ceiling = HardQuantityCeiling();
        var bulkFloor = Math.Max(CustomerSettings.QuantityMin, visit.Archetype.QuantityMin);
        var target = Round(bulkFloor * 0.40f * GroupQuantityScale(visit) * CustomerSettings.QuantityMultiplier);
        if (target >= RoundingThreshold)
            target = Math.Max(5, Round(target / 5f) * 5);

        return Math.Clamp(target, 40, ceiling);
    }

    private static float GroupQuantityScale(Visit visit)
    {
        var members = Math.Max(1, visit.MemberSlots.Count);
        var typical = Math.Max(1, visit.Archetype.DefaultMemberCount);
        return Mathf.Clamp(0.75f + (0.25f * members / (float)typical), 0.75f, 1.50f);
    }

    private static float RankScale()
    {
        try
        {
            return Mathf.Clamp(LevelManager.GetOrderLimitMultiplier(LevelManager.CurrentRank), 1f, 10f);
        }
        catch
        {
            return 1f;
        }
    }

    private static float Payment(ProductDefinition product, int quantity, Quality quality, Archetype archetype)
    {
        float market;
        try
        {
            market = Mathf.Max(1f, product.MarketValue);
        }
        catch
        {
            market = 1f;
        }

        // Market value, not the player's listed price. Anchoring to the listed price would let a
        // player who prices at 5x market sell to the group at 4x, which is the opposite of a haircut.
        var unit = market * QualityFactor(quality) * archetype.PriceMultiplier * CustomerSettings.GlobalPriceMultiplier;
        return Mathf.Max(1f, unit * quantity);
    }

    private static int Round(float value) => Mathf.RoundToInt(value);

    internal readonly struct Line
    {
        internal Line(ProductDefinition product, int quantity, Quality minQuality, float payment)
        {
            Product = product;
            Quantity = quantity;
            MinQuality = minQuality;
            Payment = payment;
        }

        internal ProductDefinition Product { get; }

        internal int Quantity { get; }

        internal Quality MinQuality { get; }

        internal float Payment { get; }
    }

    internal readonly struct Result
    {
        internal static readonly Result None = default;

        internal Result(bool sent, int quantity, float payment, Quality minQuality, string products)
        {
            Sent = sent;
            Quantity = quantity;
            Payment = payment;
            MinQuality = minQuality;
            Products = products;
        }

        internal bool Sent { get; }

        internal int Quantity { get; }

        internal float Payment { get; }

        internal Quality MinQuality { get; }

        internal string Products { get; }
    }
}
