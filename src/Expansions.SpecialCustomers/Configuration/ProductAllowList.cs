using Expansions.Core.Configuration;
using S1API.Items;
using S1API.Products;

namespace Expansions.SpecialCustomers.Configuration;

/// <summary>
/// The list of products special customers are allowed to ask for.
/// <para>
/// Config-driven and comma-separated, so the owner can add a fifth custom mix-in by editing
/// <c>UserData/Expansions.cfg</c> and reloading a save. Names are matched case-insensitively against
/// every product definition the game knows about — including ones created by other mods, which is
/// the point: the default list already contains one. A name that matches nothing is reported by name
/// and skipped, never guessed at, because a contract for a product that does not exist is a contract
/// the player can never fulfil.
/// </para>
/// <para>
/// An empty list means "no restriction", which is the escape hatch: clear the setting and the group
/// goes back to buying anything the player has listed.
/// </para>
/// </summary>
internal static class ProductAllowList
{
    internal const string ConfigKey = "product_allow_list";

    /// <summary>Seeded with the owner's three; the fourth mix-in goes in the config file.</summary>
    internal const string DefaultList = "Granddaddy Purple, Cocaine, Diddy's Powdered Semen";

    /// <summary>
    /// How long a resolution with unmatched names is trusted before it is recomputed. Modded product
    /// definitions can register after a save has finished loading, so a miss has to be retryable —
    /// but not on every single read, because resolving walks the whole item registry.
    /// </summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(20);

    private static readonly object Gate = new();

    private static ConfigValue<string>? _entry;
    private static Resolution? _cache;
    private static DateTime _resolvedAt = DateTime.MinValue;

    internal static void Bind(ModuleConfig config)
    {
        _entry = config.Bind(
            ConfigKey,
            DefaultList,
            "Product allow-list",
            "Comma-separated product names the visiting groups may order. Matched case-insensitively " +
            "against every product the game knows about, including ones added by other mods, and " +
            "against the internal id as a fallback. A name that matches nothing is logged and skipped. " +
            "Leave it empty to let groups buy anything you have listed for sale.");

        _entry.Changed += (_, _) => Invalidate();
    }

    /// <summary>The raw config text, for logs and the menu action.</summary>
    internal static string ConfiguredText => (_entry?.Value ?? DefaultList).Trim();

    /// <summary>The names as written, in config order, de-duplicated.</summary>
    internal static IReadOnlyList<string> ConfiguredNames => Split(ConfiguredText);

    /// <summary>False when the setting is empty, which turns the restriction off entirely.</summary>
    internal static bool IsRestricted => ConfiguredNames.Count > 0;

    /// <summary>Forces the next read to re-resolve. Called on a config edit and on a scene change.</summary>
    internal static void Invalidate()
    {
        lock (Gate)
        {
            _cache = null;
            _resolvedAt = DateTime.MinValue;
        }
    }

    /// <summary>
    /// The resolved list, cached. Recomputed when the config text changes, when the module asks for
    /// it explicitly, and — while anything is still unmatched — at most once every twenty seconds.
    /// </summary>
    internal static Resolution Current(bool forceRefresh = false)
    {
        var text = ConfiguredText;

        lock (Gate)
        {
            if (!forceRefresh &&
                _cache is { } cached &&
                string.Equals(cached.ConfiguredText, text, StringComparison.Ordinal) &&
                (cached.Unmatched.Count == 0 || DateTime.UtcNow - _resolvedAt < RetryAfter))
            {
                return cached;
            }
        }

        var resolution = Build(text);

        lock (Gate)
        {
            _cache = resolution;
            _resolvedAt = DateTime.UtcNow;
        }

        return resolution;
    }

    /// <summary>
    /// Writes the resolved set to the log so it is obvious what matched, and says what to do about
    /// anything that did not. Called once per save load and by the menu action.
    /// </summary>
    internal static void Report(Resolution resolution)
    {
        if (!resolution.IsRestricted)
        {
            VisitorLog.Instance.Msg(
                $"Product allow-list is empty, so visiting groups may order anything you have listed for sale. " +
                $"Set {ConfigKey} in UserData/Expansions.cfg [SpecialCustomers_01_Main] to restrict them.");
            return;
        }

        if (resolution.Matched.Count > 0)
        {
            var names = resolution.Matched.Select(match =>
                $"{match.Product.Name}{(match.IsAvailableToPlayer ? string.Empty : " (not discovered yet)")}");

            VisitorLog.Instance.Msg(
                $"Product allow-list resolved {resolution.Matched.Count} of {resolution.Requested.Count}: " +
                string.Join(", ", names) + $". Searched {resolution.KnownProductCount} product definition(s).");
        }

        foreach (var miss in resolution.Unmatched)
        {
            VisitorLog.Instance.Warn(
                $"Product allow-list entry '{miss}' does not match any product definition on this save, so it is " +
                "skipped. Check the spelling against the product's name in the Products app — the match is " +
                "case-insensitive and ignores punctuation, but it has to be the whole name. A product added by " +
                "another mod only resolves once that mod has registered it, which can be after the save loads.");
        }

        if (resolution.Matched.Count == 0)
        {
            VisitorLog.Instance.Warn(
                "Nothing in the product allow-list resolved, so no group can place a bulk order. Fix the names, " +
                $"or clear {ConfigKey} to let them buy anything you have listed.");
        }
    }

    private static Resolution Build(string text)
    {
        var requested = Split(text);
        if (requested.Count == 0)
            return new Resolution(text, requested, Array.Empty<Match>(), Array.Empty<string>(), 0);

        var known = KnownProducts(out var failure);
        if (known.Count == 0)
        {
            return new Resolution(
                text,
                requested,
                Array.Empty<Match>(),
                requested,
                0,
                failure.Length > 0
                    ? $"the game's product registry could not be read ({failure})"
                    : "the game's product registry is empty, which means no save is loaded yet");
        }

        var matched = new List<Match>(requested.Count);
        var unmatched = new List<string>();

        foreach (var name in requested)
        {
            var product = FindProduct(known, name);
            if (product is null)
            {
                unmatched.Add(name);
                continue;
            }

            if (matched.Any(existing => ReferenceEquals(existing.Product, product.Value.Definition)))
                continue;

            matched.Add(new Match(name, product.Value.Definition, product.Value.Available, product.Value.MatchedOn));
        }

        return new Resolution(text, requested, matched, unmatched, known.Count);
    }

    private static Candidate? FindProduct(IReadOnlyList<Candidate> known, string name)
    {
        foreach (var candidate in known)
        {
            if (Equal(candidate.Name, name))
                return candidate with { MatchedOn = "name" };
        }

        foreach (var candidate in known)
        {
            if (Equal(candidate.Id, name))
                return candidate with { MatchedOn = "id" };
        }

        // Last pass ignores spacing, punctuation and case, so "Diddy's Powdered Semen" still finds a
        // definition whose id is "diddyspowderedsemen".
        var slug = Slug(name);
        if (slug.Length == 0)
            return null;

        foreach (var candidate in known)
        {
            if (string.Equals(Slug(candidate.Name), slug, StringComparison.Ordinal) ||
                string.Equals(Slug(candidate.Id), slug, StringComparison.Ordinal))
            {
                return candidate with { MatchedOn = "relaxed" };
            }
        }

        return null;
    }

    /// <summary>
    /// Every product definition the process can see. The item registry is the widest net — it is
    /// where both vanilla products and mod-created ones end up — and the three
    /// <c>ProductManager</c> arrays are added on top because they also tell us what the player has
    /// actually discovered.
    /// </summary>
    private static IReadOnlyList<Candidate> KnownProducts(out string failure)
    {
        failure = string.Empty;

        var byKey = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>(3);

        Collect(() => ProductManager.ListedProducts, available: true);
        Collect(() => ProductManager.DiscoveredProducts, available: true);
        Collect(() => ProductManager.FavouritedProducts, available: true);

        try
        {
            var all = ItemManager.GetAllItemDefinitions();
            if (all is not null)
            {
                foreach (var definition in all)
                {
                    if (definition is ProductDefinition product)
                        Add(product, available: false);
                }
            }
        }
        catch (Exception ex)
        {
            problems.Add($"ItemManager.GetAllItemDefinitions: {Describe.Of(ex)}");
        }

        if (byKey.Count == 0 && problems.Count > 0)
            failure = string.Join("; ", problems);

        return byKey.Values.ToArray();

        void Collect(Func<ProductDefinition[]?> read, bool available)
        {
            try
            {
                var products = read();
                if (products is null)
                    return;

                foreach (var product in products)
                {
                    if (product is not null)
                        Add(product, available);
                }
            }
            catch (Exception ex)
            {
                problems.Add(Describe.Of(ex));
            }
        }

        void Add(ProductDefinition product, bool available)
        {
            string id;
            string name;

            try
            {
                id = product.ID ?? string.Empty;
                name = product.Name ?? string.Empty;
            }
            catch
            {
                // A definition whose own identity cannot be read is not one a contract can name.
                return;
            }

            var key = id.Length > 0 ? id : name;
            if (key.Length == 0)
                return;

            if (byKey.TryGetValue(key, out var existing))
            {
                if (available && !existing.Available)
                    byKey[key] = existing with { Available = true };

                return;
            }

            byKey[key] = new Candidate(id, name, product, available, string.Empty);
        }
    }

    private static IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var names = new List<string>(4);
        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim();
            if (trimmed.Length > 0 && !names.Any(existing => Equal(existing, trimmed)))
                names.Add(trimmed);
        }

        return names;
    }

    private static bool Equal(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Slug(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var buffer = new char[value!.Length];
        var length = 0;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
                buffer[length++] = char.ToLowerInvariant(c);
        }

        return new string(buffer, 0, length);
    }

    private readonly record struct Candidate(
        string Id,
        string Name,
        ProductDefinition Definition,
        bool Available,
        string MatchedOn);

    /// <summary>One configured name and the definition it resolved to.</summary>
    internal readonly struct Match
    {
        internal Match(string requestedName, ProductDefinition product, bool isAvailableToPlayer, string matchedOn)
        {
            RequestedName = requestedName;
            Product = product;
            IsAvailableToPlayer = isAvailableToPlayer;
            MatchedOn = matchedOn;
        }

        internal string RequestedName { get; }

        internal ProductDefinition Product { get; }

        /// <summary>True when the player has discovered, listed or favourited it.</summary>
        internal bool IsAvailableToPlayer { get; }

        /// <summary>Which pass matched: <c>name</c>, <c>id</c> or <c>relaxed</c>.</summary>
        internal string MatchedOn { get; }
    }

    /// <summary>The outcome of one resolution pass.</summary>
    internal sealed class Resolution
    {
        internal Resolution(
            string configuredText,
            IReadOnlyList<string> requested,
            IReadOnlyList<Match> matched,
            IReadOnlyList<string> unmatched,
            int knownProductCount,
            string registryFailure = "")
        {
            ConfiguredText = configuredText;
            Requested = requested;
            Matched = matched;
            Unmatched = unmatched;
            KnownProductCount = knownProductCount;
            RegistryFailure = registryFailure;
        }

        internal string ConfiguredText { get; }

        internal IReadOnlyList<string> Requested { get; }

        internal IReadOnlyList<Match> Matched { get; }

        internal IReadOnlyList<string> Unmatched { get; }

        internal int KnownProductCount { get; }

        /// <summary>Non-empty when the registry itself could not be read, rather than a name missing.</summary>
        internal string RegistryFailure { get; }

        internal bool IsRestricted => Requested.Count > 0;

        internal IReadOnlyList<ProductDefinition> Products =>
            Matched.Select(match => match.Product).ToArray();

        /// <summary>The subset the player has actually discovered, which is what a group asks for first.</summary>
        internal IReadOnlyList<ProductDefinition> AvailableProducts =>
            Matched.Where(match => match.IsAvailableToPlayer).Select(match => match.Product).ToArray();

        /// <summary>One sentence naming why no contract can be built, or empty when one can.</summary>
        internal string BlockingReason
        {
            get
            {
                if (!IsRestricted || Matched.Count > 0)
                    return string.Empty;

                if (RegistryFailure.Length > 0)
                    return $"the product allow-list could not be resolved because {RegistryFailure}";

                return
                    $"none of the {Requested.Count} name(s) in {ConfigKey} matched a product definition " +
                    $"(tried: {string.Join(", ", Requested)}). Fix them in UserData/Expansions.cfg under " +
                    $"[SpecialCustomers_01_Main], or clear {ConfigKey} to let the group buy anything you have listed";
            }
        }
    }
}
