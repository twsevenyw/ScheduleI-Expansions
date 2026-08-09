using Expansions.Core.Diagnostics;
using UnityEngine;

namespace Expansions.SpecialCustomers.Game;

/// <summary>
/// Late-bound handle to the game-side half of a visitor: the pieces S1API does not expose.
/// <para>
/// S1API covers movement, messaging, relationships and the customer offer. Visibility, the avatar
/// itself and the live <c>CustomerData</c> it does not, and reaching them is what re-dressing and
/// parking need. Everything here degrades to <c>false</c> plus a reason rather than throwing.
/// </para>
/// </summary>
internal sealed class GameNpc
{
    private readonly object _npc;

    private GameNpc(object npc, string id)
    {
        _npc = npc;
        Id = id;
    }

    internal string Id { get; }

    /// <summary>The game-side NPC component, for the helpers that need the whole object.</summary>
    internal object Native => _npc;

    /// <summary>
    /// True once the player has met them. A locked customer is refused a contract offer by the
    /// shipped code without saying so, which is why every visitor is unlocked on arrival.
    /// </summary>
    internal bool IsUnlocked =>
        GameReflection.TryReadPath(_npc, "RelationData.Unlocked", out var value, out _) && value is true;

    /// <summary>
    /// The NPC's phone conversation. This is where a contract offer has to land — a message with no
    /// accept/reject responses attached is an offer the player cannot answer.
    /// </summary>
    internal object? Conversation =>
        GameReflection.TryRead(_npc, "MSGConversation", out var value, out _) && GameReflection.IsPresent(value)
            ? value
            : null;

    /// <summary>The dialogue handler the in-world interaction menu is built on.</summary>
    internal object? DialogueHandler
    {
        get
        {
            if (!GameReflection.TryRead(_npc, "DialogueHandler", out var value, out _) ||
                !GameReflection.IsPresent(value))
            {
                return null;
            }

            // Base-typed fields / GetComponent shells hide DialogueHandler members until cast.
            return InteropCast.As(value, GameTypes.DialogueHandler) ?? value;
        }
    }

    internal GameObject? GameObject =>
        GameReflection.TryRead(_npc, "gameObject", out var value, out _) && value is GameObject gameObject && gameObject != null
            ? gameObject
            : null;

    /// <summary>Resolves through the public static registry S1API itself keys off.</summary>
    internal static GameNpc? Resolve(string id, out string failure)
    {
        var managerType = GameReflection.FindType(GameTypes.NpcManager);
        if (managerType is null)
        {
            failure = $"'{GameTypes.NpcManager}' not found";
            return null;
        }

        if (!GameReflection.TryInvoke(managerType, null, "GetNPC", new object?[] { id }, out var npc, out failure))
            return null;

        if (!GameReflection.IsPresent(npc))
        {
            failure = "the NPC registry has no entry with that id";
            return null;
        }

        // GetNPC can hand back an Object/Component shell depending on the interop signature.
        var typed = InteropCast.As(npc, GameTypes.Npc) ?? npc!;

        failure = string.Empty;
        return new GameNpc(typed, id);
    }

    /// <summary>
    /// Shows or hides the visitor. <c>networked: true</c> is not optional — a host-only hide leaves
    /// the group standing in the street on every other client.
    /// </summary>
    internal bool SetVisible(bool visible, out string failure)
    {
        var npcType = GameReflection.FindType(GameTypes.Npc) ?? _npc.GetType();
        var typed = InteropCast.As(_npc, npcType) ?? _npc;
        return GameReflection.TryInvoke(npcType, typed, "SetVisible", new object?[] { visible, true }, out _, out failure);
    }

    internal bool IsVisible =>
        GameReflection.TryRead(_npc, "isVisible", out var value, out _) && value is true;

    /// <summary>
    /// Keeps <c>NPCManager.GetNPCsInRegion</c> and the map agreeing with where the group actually
    /// stands. Written through the game enum, because the S1API mirror is a different CLR type.
    /// </summary>
    internal bool SetRegion(int region, out string failure)
    {
        var value = GameTypes.ToEnum(GameTypes.MapRegion, region);
        if (value is null)
        {
            failure = $"'{GameTypes.MapRegion}' not found";
            return false;
        }

        try
        {
            var npcType = GameReflection.FindType(GameTypes.Npc) ?? _npc.GetType();
            var typed = InteropCast.As(_npc, npcType) ?? _npc;
            var member = npcType.GetProperty("Region");
            if (member is null || !member.CanWrite)
            {
                failure = $"NPC.Region is not writable on {npcType.Name} (runtime wrapper was {_npc.GetType().Name})";
                return false;
            }

            member.SetValue(typed, value);
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = Describe.Of(ex);
            return false;
        }
    }

    internal object? Avatar
    {
        get
        {
            if (!GameReflection.TryRead(_npc, "Avatar", out var avatar, out _) ||
                !GameReflection.IsPresent(avatar))
            {
                return null;
            }

            return InteropCast.As(avatar, GameTypes.Avatar) ?? avatar;
        }
    }

    /// <summary>The avatar's live settings asset — also the donor every archetype clone is built from.</summary>
    internal object? CurrentAvatarSettings
    {
        get
        {
            var avatar = Avatar;
            if (avatar is null ||
                !GameReflection.TryRead(avatar, "CurrentSettings", out var settings, out _) ||
                !GameReflection.IsPresent(settings))
            {
                return null;
            }

            // CurrentSettings is often projected as Object/ScriptableObject — cast before cloning.
            return InteropCast.As(settings, GameTypes.AvatarSettings) ?? settings;
        }
    }

    /// <summary>Wholesale appearance replacement. The shipped entry point; nothing is appended.</summary>
    internal bool LoadAvatarSettings(object settings, out string failure)
    {
        var avatarType = GameReflection.FindType(GameTypes.Avatar);
        var avatar = Avatar;
        if (avatar is null || avatarType is null)
        {
            failure = "the NPC has no Avatar component";
            return false;
        }

        var typedAvatar = InteropCast.As(avatar, avatarType) ?? avatar;
        var typedSettings = InteropCast.As(settings, GameTypes.AvatarSettings);
        if (typedSettings is null)
        {
            failure =
                "LoadAvatarSettings refused an untyped UnityEngine.Object — cast to AvatarSettings first " +
                $"(got {settings.GetType().Name})";
            return false;
        }

        return GameReflection.TryInvoke(
            avatarType, typedAvatar, "LoadAvatarSettings", new[] { typedSettings }, out _, out failure);
    }

    internal Vector3 Position
    {
        get
        {
            try
            {
                return GameReflection.TryReadPath(_npc, "transform.position", out var value, out _) && value is Vector3 position
                    ? position
                    : Vector3.zero;
            }
            catch
            {
                return Vector3.zero;
            }
        }
    }

    /// <summary>
    /// The <c>Customer</c> behaviour on this NPC.
    /// <para>
    /// Found through the game's own static registries rather than <c>GetComponent</c>: they are
    /// public, they are what the shipped economy code itself reads, and they do not require
    /// projecting a managed <c>Type</c> into IL2CPP. A customer sits in exactly one of the two
    /// depending on whether the player has met them, so both are scanned.
    /// </para>
    /// </summary>
    internal object? Customer(out string failure)
    {
        var customerType = GameReflection.FindType(GameTypes.Customer);
        if (customerType is null)
        {
            failure = $"'{GameTypes.Customer}' not found";
            return null;
        }

        foreach (var listName in new[] { "UnlockedCustomers", "LockedCustomers" })
        {
            if (!GameReflection.TryReadStatic(customerType, listName, out var list, out _) || list is null)
                continue;

            foreach (var candidate in GameReflection.Enumerate(list))
            {
                if (!GameReflection.IsPresent(candidate))
                    continue;

                if (GameReflection.TryReadPath(candidate, "NPC.ID", out var id, out _) &&
                    id is string text &&
                    string.Equals(text, Id, StringComparison.Ordinal))
                {
                    failure = string.Empty;
                    return InteropCast.As(candidate, customerType) ?? candidate;
                }
            }
        }

        // Fallback for the case the registries do not cover: a Customer whose Awake ran under
        // conditions that skipped registration is still a component on the NPC's GameObject.
        var component = CustomerComponent(customerType);
        if (component is not null)
        {
            failure = string.Empty;
            return component;
        }

        failure = "no Customer is registered for this NPC id, and none is attached to its GameObject";
        return null;
    }

    /// <summary>
    /// <c>GetComponent</c> through the IL2CPP type projection. Kept as the second attempt rather
    /// than the first because it needs a managed-to-IL2CPP type conversion that the registry scan
    /// does not, and the registries are what the shipped economy code itself reads.
    /// </summary>
    private object? CustomerComponent(Type customerType)
    {
        try
        {
            if (!GameReflection.TryRead(_npc, "gameObject", out var value, out _) ||
                value is not GameObject gameObject ||
                gameObject == null)
            {
                return null;
            }

            var component = gameObject.GetComponent(Il2CppInterop.Runtime.Il2CppType.From(customerType));
            if (!GameReflection.IsPresent(component))
                return null;

            // GetComponent(Il2CppType) returns an Object wrapper — Customer members are invisible on it.
            return InteropCast.As(component, customerType) ?? component;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The mutable per-visitor tuning asset. One per pool type, so writing it is safe.</summary>
    internal object? CustomerData(out string failure)
    {
        var customer = Customer(out failure);
        if (customer is null)
            return null;

        if (!GameReflection.TryRead(customer, "customerData", out var data, out failure) ||
            !GameReflection.IsPresent(data))
        {
            failure = failure.Length > 0 ? failure : "Customer.customerData is null";
            return null;
        }

        failure = string.Empty;
        return InteropCast.As(data, GameTypes.CustomerData) ?? data;
    }

    /// <summary>Turns the standard "potential customer" map marker on or off for this visitor.</summary>
    internal bool SetPotentialCustomerPoiEnabled(bool enabled, out string failure)
    {
        var customer = Customer(out failure);
        if (customer is null)
            return false;

        return GameReflection.TryInvoke(
            customer.GetType(), customer, "SetPotentialCustomerPoIEnabled", new object?[] { enabled }, out _, out failure);
    }

    /// <summary>
    /// True once the player has accepted this customer's offer and the game has materialised a real
    /// contract for it. Polling this is how the tutorial notices an acceptance without patching the
    /// shipped contract pipeline.
    /// </summary>
    internal bool HasLiveContract
    {
        get
        {
            var customer = Customer(out _);
            return customer is not null &&
                   GameReflection.TryRead(customer, "CurrentContract", out var contract, out _) &&
                   GameReflection.IsPresent(contract);
        }
    }

    /// <summary>
    /// The game's own count of handovers this customer has completed. Rises only when the shipped
    /// pipeline has evaluated a delivery and paid for it, which makes it a far better "you got paid"
    /// signal than watching the cash balance — a player who buys something first would otherwise
    /// have to earn back past their own spending before the objective ticked off.
    /// </summary>
    internal int CompletedDeliveries
    {
        get
        {
            var customer = Customer(out _);
            return customer is not null &&
                   GameReflection.TryRead(customer, "CompletedDeliveries", out var value, out _) &&
                   value is int count
                ? count
                : 0;
        }
    }

    /// <summary>Whether the shipped generator currently thinks this customer would place an order.</summary>
    internal bool ShouldTryGenerateDeal(out string failure)
    {
        var customer = Customer(out failure);
        if (customer is null)
            return false;

        return GameReflection.TryInvoke(
                   customer.GetType(), customer, "ShouldTryGenerateDeal", Array.Empty<object?>(), out var value, out failure) &&
               value is true;
    }
}
