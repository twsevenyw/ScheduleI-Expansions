using Expansions.Core.Diagnostics;

namespace Expansions.PoliceOverhaul.Runtime;

/// <summary>
/// Delivers Dispatch announcements to the peer they concern without custom FishNet RPCs.
/// <para>
/// <c>NotificationsManager.SendNotification</c> is local UI only — the host toasting never reaches a
/// co-op client. FishNet's weaver never ran on this assembly, so there is no TargetRpc we can add.
/// The shipped Steam lobby side-channel (<c>Lobby.SetLobbyData</c> /
/// <c>ILobbyService.GetLobbyData</c>) is the verified mod↔mod path: the host publishes a compact
/// payload, and every client polls it and toasts locally when the target key matches.
/// </para>
/// </summary>
internal static class AnnounceRelay
{
    internal const string WorldTarget = "*";
    private const string LobbyKey = "exp_po_ann";
    private const string LobbyType = "Il2CppScheduleOne.Networking.Lobby";
    private const float ClientPollSeconds = 0.35f;
    private const int MaxLobbyBodyChars = 480;

    private static int _seq;
    private static string _lastConsumed = string.Empty;
    private static float _nextPollAt;
    private static bool _clientAttached;
    private static string _route = "unset";
    private static string _peerRole = "unknown";

    /// <summary>Probe / log: how the last delivery was routed.</summary>
    internal static string RouteInUse => _route;

    /// <summary>Probe: host / client / single-player as last evaluated.</summary>
    internal static string PeerRole => _peerRole;

    internal static int Sequence => _seq;

    internal static string LastLobbyPayload { get; private set; } = string.Empty;

    /// <summary>Client-only: start polling lobby data for host-published announcements.</summary>
    internal static void AttachClient()
    {
        _clientAttached = true;
        _lastConsumed = string.Empty;
        _nextPollAt = 0f;
        _peerRole = "client";
        _route = "steam-lobby-poll (client)";
        PoliceLog.Msg(
            "Announcement route: Steam lobby poll on this client peer. " +
            "Host cannot push NotificationsManager toasts across the wire — no custom RPCs.");
    }

    internal static void Detach()
    {
        _clientAttached = false;
        _lastConsumed = string.Empty;
        _nextPollAt = 0f;
    }

    /// <summary>
    /// Deliver a toast to the local player when they are the audience, and (on the host in co-op)
    /// publish the same line so remote peers can toast it themselves.
    /// </summary>
    /// <param name="targetKey">
    /// <see cref="WorldTarget"/> for everyone, or a <c>PlayerCode</c> / <c>"local"</c> key for one player.
    /// </param>
    internal static void Deliver(string title, string body, float seconds, string targetKey, bool bypassHudGate)
    {
        RefreshPeerRole();

        if (string.IsNullOrWhiteSpace(body))
            return;

        var key = string.IsNullOrWhiteSpace(targetKey) ? WorldTarget : targetKey.Trim();
        var heading = string.IsNullOrWhiteSpace(title) ? DispatchContact.DisplayName : title.Trim();
        var text = body.Trim();

        var localKey = GameBridge.KeyFor(GameBridge.LocalPlayer());
        var concernsLocal = key == WorldTarget ||
                            string.Equals(key, localKey, StringComparison.Ordinal) ||
                            string.Equals(key, "local", StringComparison.Ordinal);

        // Solo / no lobby: local toast is the whole path.
        if (!IsNetworkedSession())
        {
            _route = "local-toast (single-player)";
            if (concernsLocal)
                ToastLocal(heading, text, seconds, bypassHudGate);
            return;
        }

        if (HostGate.IsAuthority)
        {
            if (concernsLocal)
            {
                _route = "local-toast + steam-lobby-publish (host)";
                ToastLocal(heading, text, seconds, bypassHudGate);
            }
            else
            {
                _route = "steam-lobby-publish (host→remote)";
            }

            PublishLobby(key, heading, text, seconds);
            return;
        }

        // Client path should normally arrive via Poll; direct Deliver here is a local-only fallback
        // (e.g. a future client-observed announcement).
        _route = "local-toast (client-direct)";
        if (concernsLocal)
            ToastLocal(heading, text, seconds, bypassHudGate);
    }

    /// <summary>Client tick: pull the host's latest lobby payload and toast when it concerns us.</summary>
    internal static void Poll()
    {
        if (!_clientAttached || HostGate.IsAuthority)
            return;

        RefreshPeerRole();

        var now = UnityEngine.Time.unscaledTime;
        if (now < _nextPollAt)
            return;

        _nextPollAt = now + ClientPollSeconds;

        if (!TryReadLobby(out var payload) || string.IsNullOrEmpty(payload))
            return;

        if (string.Equals(payload, _lastConsumed, StringComparison.Ordinal))
            return;

        if (!TryParse(payload, out var seq, out var target, out var seconds, out var title, out var body))
        {
            PoliceLog.Detail($"AnnounceRelay: ignored malformed lobby payload ({payload.Length} chars).");
            _lastConsumed = payload;
            return;
        }

        // Host also consumes its own publish if it polls — skip by seq we already know, and by
        // never attaching the client poller on authority.
        _lastConsumed = payload;
        LastLobbyPayload = payload;
        _seq = Math.Max(_seq, seq);
        _route = $"steam-lobby-poll seq={seq} target={target}";

        var localKey = GameBridge.KeyFor(GameBridge.LocalPlayer());
        var concernsLocal = target == WorldTarget ||
                            string.Equals(target, localKey, StringComparison.Ordinal) ||
                            string.Equals(target, "local", StringComparison.Ordinal);

        if (!concernsLocal)
        {
            PoliceLog.Detail($"AnnounceRelay: skipped seq={seq} (target '{target}', local '{localKey}').");
            return;
        }

        ToastLocal(title, body, seconds, bypassHudGate: true);
        PoliceLog.Detail($"AnnounceRelay: client toasted seq={seq} '{title}'.");
    }

    private static void ToastLocal(string title, string body, float seconds, bool bypassHudGate)
    {
        if (!bypassHudGate && PoliceRuntime.Config is { ShowHud.Value: false })
            return;

        GameBridge.Notify(title, body, seconds);
    }

    private static void PublishLobby(string targetKey, string title, string body, float seconds)
    {
        _seq++;
        var clipped = body.Length <= MaxLobbyBodyChars ? body : body[..(MaxLobbyBodyChars - 1)] + "…";
        // Tabs are unlikely in our copy; if present, flatten so parsing stays trivial.
        var payload =
            $"v1\t{_seq}\t{Sanitize(targetKey)}\t{seconds:0.##}\t{Sanitize(title)}\t{Sanitize(clipped)}";

        LastLobbyPayload = payload;

        if (!TryWriteLobby(payload))
        {
            PoliceLog.Warn(
                "AnnounceRelay: could not publish to Steam lobby data — remote peers will not see this " +
                "announcement. Local toast (if any) still fired.");
            _route += " [lobby-publish-failed]";
            return;
        }

        PoliceLog.Detail($"AnnounceRelay: published seq={_seq} target={targetKey} ({payload.Length} chars).");
    }

    private static string Sanitize(string value) =>
        value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    private static bool TryParse(
        string payload,
        out int seq,
        out string target,
        out float seconds,
        out string title,
        out string body)
    {
        seq = 0;
        target = WorldTarget;
        seconds = 8f;
        title = DispatchContact.DisplayName;
        body = string.Empty;

        var parts = payload.Split('\t');
        if (parts.Length < 6 || !string.Equals(parts[0], "v1", StringComparison.Ordinal))
            return false;

        if (!int.TryParse(parts[1], out seq))
            return false;

        target = parts[2];
        if (!float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out seconds))
            seconds = 8f;

        title = parts[4];
        body = parts[5];
        return body.Length > 0;
    }

    private static bool IsNetworkedSession()
    {
        var finder = GameReflection.FindType(GameTypes.InstanceFinder);
        if (finder is null)
            return false;

        return GameReflection.TryReadStatic(finder, "NetworkManager", out var manager, out _) &&
               GameReflection.IsPresent(manager);
    }

    private static void RefreshPeerRole()
    {
        if (!IsNetworkedSession())
        {
            _peerRole = "single-player";
            return;
        }

        _peerRole = HostGate.Evaluate(out var reason) ? $"host ({reason})" : reason;
    }

    private static bool TryWriteLobby(string payload)
    {
        try
        {
            var lobby = GameBridge.Singleton(LobbyType);
            if (lobby is null)
                return false;

            // Prefer the Lobby facade; fall back to the service.
            if (Members.Invoke(lobby, "SetLobbyData", LobbyKey, payload))
                return true;

            var service = Members.ReadPath(lobby, "_lobbyService");
            return service is not null && Members.Invoke(service, "SetLobbyData", LobbyKey, payload);
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"AnnounceRelay SetLobbyData failed: {PoliceLog.Describe(ex)}");
            return false;
        }
    }

    private static bool TryReadLobby(out string payload)
    {
        payload = string.Empty;
        try
        {
            var lobby = GameBridge.Singleton(LobbyType);
            if (lobby is null)
                return false;

            var service = Members.ReadPath(lobby, "_lobbyService");
            if (service is null)
                return false;

            if (Members.InvokeFor(service, "GetLobbyData", LobbyKey) is string value && value.Length > 0)
            {
                payload = value;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            PoliceLog.Detail($"AnnounceRelay GetLobbyData failed: {PoliceLog.Describe(ex)}");
            return false;
        }
    }
}
