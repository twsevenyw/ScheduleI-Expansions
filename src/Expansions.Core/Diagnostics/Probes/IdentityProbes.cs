namespace Expansions.Core.Diagnostics.Probes;

/// <summary>
/// Persistence identity. The Police plan keys per-player heat on <c>Player.PlayerCode</c>; nothing
/// proves that code is stable for the same human across sessions, and one run cannot prove it either
/// — this probe exists so two runs can be diffed.
/// </summary>
internal static class IdentityProbes
{
    private const string PlayerType = "Il2CppScheduleOne.PlayerScripts.Player";
    private const string LobbyType = "Il2CppScheduleOne.Networking.Lobby";
    private const string LoadManagerType = "Il2CppScheduleOne.Persistence.LoadManager";

    internal static void Register(List<IProbe> probes)
    {
        probes.Add(new DelegateProbe(
            "identity.player_code",
            "What identifies this player and this save on disk?",
            Areas.Identity,
            PlayerIdentity));
    }

    private static void PlayerIdentity(ProbeContext context, ProbeResult result)
    {
        var playerType = GameReflection.FindType(PlayerType);
        if (playerType is null)
        {
            result.NotFound($"`{PlayerType}` not found; per-player persistence has no key on this build.");
            return;
        }

        var rows = new List<IReadOnlyList<string>>();

        var hasLocal = GameReflection.TryReadStatic(playerType, "Local", out var local, out var localFailure) &&
                       GameReflection.IsPresent(local);

        if (hasLocal)
        {
            rows.Add(new[] { "Player.Local.PlayerCode", ProbeHelpers.ReadString(local, "PlayerCode"), "the key the Police plan wants to use" });
            rows.Add(new[] { "Player.Local.PlayerName", ProbeHelpers.ReadString(local, "PlayerName"), string.Empty });
            rows.Add(new[] { "Player.Local.Connection.ClientId", ReadConnectionId(local), "per-session only, never a persistence key" });
        }
        else
        {
            rows.Add(new[] { "Player.Local", "<" + (localFailure.Length > 0 ? localFailure : "null") + ">", "no local player yet" });
        }

        rows.Add(new[] { "Steam id", ReadSteamId(), "stable across sessions if reachable" });

        if (GameReflection.TryGetSingleton(LoadManagerType, out var loadManager, out var loadFailure))
        {
            rows.Add(new[] { "LoadManager.LoadedGameFolderPath", ProbeHelpers.ReadString(loadManager, "LoadedGameFolderPath"), "where per-save data lands" });

            if (GameReflection.TryRead(loadManager, "ActiveSaveInfo", out var saveInfo, out _) && GameReflection.IsPresent(saveInfo))
            {
                rows.Add(new[] { "ActiveSaveInfo.SavePath", ProbeHelpers.ReadString(saveInfo, "SavePath"), string.Empty });
                rows.Add(new[] { "ActiveSaveInfo.SaveSlotNumber", ProbeHelpers.ReadFormatted(saveInfo, "SaveSlotNumber"), string.Empty });
                rows.Add(new[] { "ActiveSaveInfo.OrganisationName", ProbeHelpers.ReadString(saveInfo, "OrganisationName"), string.Empty });
            }
        }
        else
        {
            rows.Add(new[] { "LoadManager", "<" + loadFailure + ">", "no save loaded" });
        }

        if (GameReflection.TryGetSingleton(LobbyType, out var lobby, out var lobbyFailure))
        {
            rows.Add(new[] { "Lobby.IsInLobby", ProbeHelpers.ReadFormatted(lobby, "IsInLobby"), string.Empty });
            rows.Add(new[] { "Lobby.IsHost", ProbeHelpers.ReadFormatted(lobby, "IsHost"), string.Empty });
            rows.Add(new[] { "Lobby.LobbyID", ProbeHelpers.ReadFormatted(lobby, "LobbyID"), string.Empty });
            rows.Add(new[] { "Lobby.PlayerCount", ProbeHelpers.ReadFormatted(lobby, "PlayerCount"), string.Empty });
        }
        else
        {
            rows.Add(new[] { "Lobby", "<" + lobbyFailure + ">", "singleplayer, or not up yet" });
        }

        result.Table(new[] { "Source", "Value", "Note" }, rows);

        if (GameReflection.TryReadStatic(playerType, "PlayerList", out var playerList, out _))
        {
            var players = GameReflection.Enumerate(playerList);
            if (players.Count > 0)
            {
                result.Heading($"All players ({players.Count})");
                result.Table(
                    new[] { "PlayerCode", "PlayerName", "Is local" },
                    players.Select(p => (IReadOnlyList<string>)new[]
                    {
                        ProbeHelpers.ReadString(p, "PlayerCode"),
                        ProbeHelpers.ReadString(p, "PlayerName"),
                        ProbeHelpers.Yes(hasLocal ? ReferenceEquals(p, local) : null),
                    }).ToList());
            }
        }

        if (!hasLocal)
        {
            result.Inconclusive("No local player exists yet, so there is no identity to record. Run this from inside a loaded save.");
            return;
        }

        result.Ok(
            "Recorded. **One run cannot answer the stability question.** Load the same save a second time, run the suite again, " +
            "and diff the PlayerCode line between the two reports; then repeat with a co-op client joining in the opposite order. " +
            "If PlayerCode moves, fall back to keying single-player heat on the literal \"local\" and co-op heat on the Steam id above.");
    }

    private static string ReadConnectionId(object? player)
    {
        if (!GameReflection.TryRead(player, "Connection", out var connection, out var failure) || !GameReflection.IsPresent(connection))
            return "<" + (failure.Length > 0 ? failure : "null") + ">";

        return ProbeHelpers.ReadFormatted(connection, "ClientId");
    }

    private static string ReadSteamId()
    {
        var steamUser = GameReflection.FindType("Il2CppSteamworks.SteamUser");
        if (steamUser is null)
            return "<Steamworks interop assembly not loaded>";

        if (!GameReflection.TryInvoke(steamUser, null, "GetSteamID", Array.Empty<object?>(), out var id, out var failure))
            return "<" + failure + ">";

        return GameReflection.TryRead(id, "m_SteamID", out var raw, out _)
            ? GameReflection.Format(raw)
            : GameReflection.Format(id);
    }
}
