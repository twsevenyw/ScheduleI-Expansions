using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using S1API.Console;
using S1API.Money;
using S1API.Property;
using UnityEngine;

[assembly: MelonInfo(typeof(CreativeMode.CreativeModeMod), "Creative Mode", "1.3.0", "Evan")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonColor(255, 80, 200, 120)]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]

namespace CreativeMode;

/// <summary>
/// Minecraft-style creative toolbox. F8 toggles.
/// Uses absolute IMGUI rects only — GUILayout.TextField is stripped on IL2CPP Schedule I.
/// </summary>
public class CreativeModeMod : MelonMod
{
    private const KeyCode ToggleKey = KeyCode.F8;
    private const int WindowId = 0xC0FFEE;
    private const int PageSize = 12;

    private static readonly string[] Tabs = { "Items", "Money", "Player", "Unlock", "NPCs", "Quests", "World" };
    private static readonly string[] NpcFilters = { "All", "Suppliers", "Locked", "Unlocked" };
    private static readonly float[] TimeScalePresets = { 0f, 0.25f, 0.5f, 1f, 2f, 5f, 10f };

    private static readonly string[] ItemFilters =
    {
        "All", "A-F", "G-M", "N-S", "T-Z",
        "weed", "og", "meth", "coca", "shroom", "mushroom", "ac", "grain", "spore",
        "seed", "soil", "light", "pot", "bag", "jar", "cash", "gun", "ammo"
    };

    private bool _menuOpen;
    private int _tab;
    private int _itemPage;
    private int _filterIndex;
    private int _qty = 1;
    private string _status = "F8 opens Creative Mode.";
    private Rect _windowRect = new(60, 50, 820, 640);

    private CursorLockMode _prevLock;
    private bool _prevVisible = true;

    private string _itemSearch = "";
    private bool _searchFocused;
    private float _caretBlink;

    private List<ItemEntry> _allItems = new();
    private List<ItemEntry> _filteredItems = new();
    private ItemEntry? _selectedItem;

    private List<(string Name, string Code, bool Owned, Action Unlock)> _unlockables = new();
    private int _unlockPage;

    private List<QuestEntry> _quests = new();
    private int _questPage;

    private List<NpcEntry> _npcs = new();
    private List<NpcEntry> _filteredNpcs = new();
    private int _npcPage;
    private int _npcFilterIndex;
    private NpcEntry? _selectedNpc;
    private float _timeScale = 1f;

    private static Texture2D? _panelBg;
    private static Texture2D? _btnBg;
    private static Texture2D? _btnHover;
    private static Texture2D? _btnActive;
    private static Texture2D? _fieldBg;
    private static Texture2D? _fieldFocusBg;
    private static GUIStyle? _labelStyle;
    private static GUIStyle? _headerStyle;
    private static GUIStyle? _statusStyle;
    private static GUIStyle? _btnStyle;
    private static GUIStyle? _fieldStyle;
    private static bool _stylesReady;

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg("Creative Mode 1.3.0 loaded. F8 to open.");
    }

    public override void OnUpdate()
    {
        if (Input.GetKeyDown(ToggleKey))
            SetMenuOpen(!_menuOpen);

        if (_menuOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            if (_searchFocused)
            {
                _searchFocused = false;
            }
            else
            {
                SetMenuOpen(false);
            }
        }

        if (_menuOpen)
        {
            // Force cursor every frame — game re-locks otherwise.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_searchFocused && _tab == 0)
                HandleSearchTyping();
        }
    }

    private void HandleSearchTyping()
    {
        // IL2CPP strips GUI.TextField — capture keys manually instead.
        if (Input.GetKeyDown(KeyCode.Backspace) && _itemSearch.Length > 0)
        {
            _itemSearch = _itemSearch[..^1];
            _itemPage = 0;
            ApplyFilter();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Delete))
        {
            _itemSearch = "";
            _itemPage = 0;
            ApplyFilter();
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            _searchFocused = false;
            return;
        }

        var typed = Input.inputString;
        if (string.IsNullOrEmpty(typed))
            return;

        var changed = false;
        foreach (var c in typed)
        {
            if (c == '\b')
            {
                if (_itemSearch.Length > 0)
                {
                    _itemSearch = _itemSearch[..^1];
                    changed = true;
                }
                continue;
            }

            if (c == '\n' || c == '\r' || c < 32)
                continue;

            if (_itemSearch.Length >= 48)
                continue;

            _itemSearch += c;
            changed = true;
        }

        if (!changed)
            return;

        _itemPage = 0;
        ApplyFilter();
    }

    public override void OnGUI()
    {
        if (!_menuOpen)
            return;

        EnsureStyles();
        _windowRect = GUI.Window(WindowId, _windowRect, (GUI.WindowFunction)DrawWindow, "Creative Mode");
    }

    private void SetMenuOpen(bool open)
    {
        if (open == _menuOpen)
            return;

        if (open)
        {
            _prevLock = Cursor.lockState;
            _prevVisible = Cursor.visible;
            _menuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshItemCache(force: true);
            RefreshUnlockables();
            RefreshQuests();
            RefreshNpcs();
            SetStatus("Creative Mode open — mouse unlocked.");
        }
        else
        {
            _menuOpen = false;
            _searchFocused = false;
            Cursor.lockState = _prevLock == CursorLockMode.None ? CursorLockMode.Locked : _prevLock;
            Cursor.visible = _prevVisible;
            SetStatus("Creative Mode closed.");
        }
    }

    private void DrawWindow(int id)
    {
        // Do NOT wrap the whole window in one try/catch — a single stripped GUI call
        // would abort the rest of the tab (that blanked Items before).
        float x = 12;
        float y = 28;
        for (var i = 0; i < Tabs.Length; i++)
        {
            if (TabButton(new Rect(x, y, 96, 28), Tabs[i], _tab == i))
            {
                _tab = i;
                if (i != 0)
                    _searchFocused = false;
            }
            x += 102;
        }

        y = 66;
        try
        {
            switch (_tab)
            {
                case 0: DrawItems(y); break;
                case 1: DrawMoney(y); break;
                case 2: DrawPlayer(y); break;
                case 3: DrawUnlock(y); break;
                case 4: DrawNpcs(y); break;
                case 5: DrawQuests(y); break;
                case 6: DrawWorld(y); break;
            }
        }
        catch (Exception ex)
        {
            GUI.Label(new Rect(12, y, 700, 60), $"Tab draw error: {ex.Message}", _statusStyle);
            LoggerInstance.Error($"Tab draw error: {ex}");
        }

        GUI.Label(new Rect(12, _windowRect.height - 42, _windowRect.width - 24, 36), _status, _statusStyle);
        GUI.DragWindow(new Rect(0, 0, 10000, 24));
    }

    private void DrawItems(float y)
    {
        GUI.Label(new Rect(12, y, 400, 22), "Item Spawner", _headerStyle);
        y += 26;

        // Search box (click, then type — no TextField on IL2CPP)
        GUI.Label(new Rect(12, y, 55, 28), "Search", _labelStyle);
        var searchRect = new Rect(70, y, 420, 28);
        DrawSearchField(searchRect);
        if (Btn(new Rect(500, y, 70, 28), "Clear"))
        {
            _itemSearch = "";
            _itemPage = 0;
            ApplyFilter();
        }
        if (Btn(new Rect(578, y, 90, 28), "Refresh"))
            RefreshItemCache(force: true);
        y += 34;

        // Filters
        float fx = 12;
        for (var i = 0; i < ItemFilters.Length; i++)
        {
            if (fx > _windowRect.width - 80)
            {
                fx = 12;
                y += 28;
            }

            if (TabButton(new Rect(fx, y, 68, 24), ItemFilters[i], _filterIndex == i))
            {
                _filterIndex = i;
                _itemPage = 0;
                ApplyFilter();
            }
            fx += 72;
        }

        y += 32;

        // Qty presets
        GUI.Label(new Rect(12, y, 40, 24), "Qty", _labelStyle);
        float qx = 50;
        foreach (var q in new[] { 1, 5, 10, 20, 64 })
        {
            if (TabButton(new Rect(qx, y, 44, 24), q.ToString(), _qty == q))
                _qty = q;
            qx += 48;
        }

        y += 32;

        // Item list (paged — no scrollview / textfields)
        var start = _itemPage * PageSize;
        var pageItems = _filteredItems.Skip(start).Take(PageSize).ToList();
        var listY = y;
        for (var i = 0; i < pageItems.Count; i++)
        {
            var item = pageItems[i];
            var selected = _selectedItem != null && item.Id == _selectedItem.Id;
            var label = string.IsNullOrEmpty(item.Name) ? item.Id : $"{item.Name}  [{item.Id}]";
            if (TabButton(new Rect(12, listY + i * 26, 430, 24), label, selected))
                _selectedItem = item;
        }

        // Selected panel
        var px = 460f;
        var py = y;
        GUI.Label(new Rect(px, py, 280, 22), "Selected", _headerStyle);
        py += 26;
        if (_selectedItem == null)
        {
            GUI.Label(new Rect(px, py, 280, 40), "Pick an item on the left.", _labelStyle);
        }
        else
        {
            GUI.Label(new Rect(px, py, 280, 20), _selectedItem.Name ?? "", _labelStyle);
            py += 20;
            GUI.Label(new Rect(px, py, 280, 20), $"ID: {_selectedItem.Id}", _labelStyle);
            py += 24;
            if (Btn(new Rect(px, py, 130, 28), $"Give x{_qty}"))
                GiveSelected(_qty);
            if (Btn(new Rect(px + 140, py, 100, 28), "Give x1"))
                GiveSelected(1);
            py += 34;
            if (Btn(new Rect(px, py, 130, 28), "Give x10"))
                GiveSelected(10);
            if (Btn(new Rect(px + 140, py, 100, 28), "Give x64"))
                GiveSelected(64);
            py += 34;
            if (Btn(new Rect(px, py, 240, 28), "Fill stack"))
                GiveSelected(Math.Max(1, _selectedItem.StackLimit));
        }

        // Pagination + clear
        var bottom = y + PageSize * 26 + 8;
        var pages = Math.Max(1, (int)Math.Ceiling(_filteredItems.Count / (double)PageSize));
        if (Btn(new Rect(12, bottom, 70, 26), "< Prev") && _itemPage > 0)
            _itemPage--;
        GUI.Label(new Rect(90, bottom, 160, 26), $"Page {_itemPage + 1}/{pages}  ({_filteredItems.Count})", _labelStyle);
        if (Btn(new Rect(250, bottom, 70, 26), "Next >") && _itemPage < pages - 1)
            _itemPage++;
        if (Btn(new Rect(340, bottom, 120, 26), "Clear inventory"))
            SafeRun(() => { ConsoleHelper.ClearInventory(); SetStatus("Inventory cleared."); });
    }

    private void DrawMoney(float y)
    {
        GUI.Label(new Rect(12, y, 400, 22), "Economy", _headerStyle);
        y += 28;

        float cash = 0, online = 0;
        try { cash = Money.GetCashBalance(); online = Money.GetOnlineBalance(); } catch { /* menu */ }

        GUI.Label(new Rect(12, y, 360, 22), $"Cash: ${cash:N0}    Bank: ${online:N0}", _labelStyle);
        y += 30;

        GUI.Label(new Rect(12, y, 200, 22), "Add cash", _headerStyle);
        y += 26;
        DrawMoneyRow(ref y, "Cash", amount =>
        {
            ConsoleHelper.RunCashCommand(amount);
            SetStatus($"Cash {(amount >= 0 ? "+" : "")}${amount:N0}");
        });

        y += 10;
        GUI.Label(new Rect(12, y, 200, 22), "Add bank / online", _headerStyle);
        y += 26;
        DrawMoneyRow(ref y, "Bank", amount =>
        {
            ConsoleHelper.RunOnlineBalanceCommand(amount);
            SetStatus($"Bank {(amount >= 0 ? "+" : "")}${amount:N0}");
        });
    }

    private void DrawMoneyRow(ref float y, string prefix, Action<int> apply)
    {
        float x = 12;
        foreach (var amount in new[] { 1000, 10000, 100000, 1000000 })
        {
            var label = amount >= 1_000_000 ? $"{prefix} +$1M" : $"{prefix} +${amount / 1000}k";
            if (Btn(new Rect(x, y, 140, 30), label))
                SafeRun(() => apply(amount));
            x += 150;
        }
        y += 36;
        x = 12;
        foreach (var amount in new[] { -1000, -10000, -100000 })
        {
            if (Btn(new Rect(x, y, 140, 30), $"{prefix} ${amount}"))
                SafeRun(() => apply(amount));
            x += 150;
        }
        y += 36;
    }

    private void DrawPlayer(float y)
    {
        GUI.Label(new Rect(12, y, 400, 22), "Player", _headerStyle);
        y += 30;

        if (Btn(new Rect(12, y, 140, 32), "Full health"))
            SafeRun(() => { ConsoleHelper.SetPlayerHealth(100f); SetStatus("Health → 100"); });
        if (Btn(new Rect(160, y, 140, 32), "Clear wanted"))
            SafeRun(() => { ConsoleHelper.ClearWanted(); SetStatus("Wanted cleared"); });
        if (Btn(new Rect(308, y, 140, 32), "Lower wanted"))
            SafeRun(() => { ConsoleHelper.LowerWanted(); SetStatus("Wanted lowered"); });
        y += 42;

        if (Btn(new Rect(12, y, 140, 32), "+100 XP"))
            SafeRun(() => { ConsoleHelper.GiveXp(100); SetStatus("+100 XP"); });
        if (Btn(new Rect(160, y, 140, 32), "+1000 XP"))
            SafeRun(() => { ConsoleHelper.GiveXp(1000); SetStatus("+1000 XP"); });
        if (Btn(new Rect(308, y, 140, 32), "+10000 XP"))
            SafeRun(() => { ConsoleHelper.GiveXp(10000); SetStatus("+10000 XP"); });
        y += 50;

        GUI.Label(new Rect(12, y, 200, 22), "Move speed", _headerStyle);
        y += 26;
        float x = 12;
        foreach (var mult in new[] { 1f, 2f, 3f, 5f, 10f })
        {
            if (Btn(new Rect(x, y, 80, 28), $"×{mult:0}"))
                SafeRun(() => { ConsoleHelper.SetPlayerMoveSpeedMultiplier(mult); SetStatus($"Move ×{mult}"); });
            x += 90;
        }
        y += 40;

        GUI.Label(new Rect(12, y, 200, 22), "Jump force", _headerStyle);
        y += 26;
        x = 12;
        foreach (var mult in new[] { 1f, 2f, 3f, 5f })
        {
            if (Btn(new Rect(x, y, 80, 28), $"×{mult:0}"))
                SafeRun(() => { ConsoleHelper.SetPlayerJumpMultiplier(mult); SetStatus($"Jump ×{mult}"); });
            x += 90;
        }
    }

    private void DrawUnlock(float y)
    {
        GUI.Label(new Rect(12, y, 500, 22), "Unlock properties & businesses", _headerStyle);
        y += 28;

        if (Btn(new Rect(12, y, 180, 30), "Unlock ALL"))
        {
            SafeRun(() =>
            {
                RefreshUnlockables();
                var n = 0;
                foreach (var u in _unlockables)
                {
                    if (!u.Owned)
                    {
                        u.Unlock();
                        n++;
                    }
                }
                RefreshUnlockables();
                SetStatus($"Unlocked {n} properties/businesses.");
            });
        }
        if (Btn(new Rect(200, y, 140, 30), "Refresh list"))
            RefreshUnlockables();
        y += 40;

        const int perPage = 10;
        var start = _unlockPage * perPage;
        var page = _unlockables.Skip(start).Take(perPage).ToList();
        for (var i = 0; i < page.Count; i++)
        {
            var u = page[i];
            var label = u.Owned ? $"✓ {u.Name}  [{u.Code}]" : $"Unlock {u.Name}  [{u.Code}]";
            if (Btn(new Rect(12, y + i * 32, 520, 28), label) && !u.Owned)
            {
                SafeRun(() =>
                {
                    u.Unlock();
                    RefreshUnlockables();
                    SetStatus($"Unlocked {u.Name} ({u.Code})");
                });
            }
        }

        var pages = Math.Max(1, (int)Math.Ceiling(_unlockables.Count / (double)perPage));
        var by = y + perPage * 32 + 8;
        if (Btn(new Rect(12, by, 70, 26), "< Prev") && _unlockPage > 0)
            _unlockPage--;
        GUI.Label(new Rect(90, by, 200, 26), $"Page {_unlockPage + 1}/{pages}", _labelStyle);
        if (Btn(new Rect(250, by, 70, 26), "Next >") && _unlockPage < pages - 1)
            _unlockPage++;
    }

    private void DrawNpcs(float y)
    {
        GUI.Label(new Rect(12, y, 740, 22), "NPCs / suppliers — unlock + relationship (0–5)", _headerStyle);
        y += 28;

        if (Btn(new Rect(12, y, 170, 28), "Unlock + max ALL"))
        {
            SafeRun(() =>
            {
                NpcCatalog.UnlockAndMaxAll(_filteredNpcs, LoggerInstance);
                RefreshNpcs();
                SetStatus($"Unlocked + maxed {_filteredNpcs.Count} NPCs (current filter).");
            });
        }
        if (Btn(new Rect(190, y, 130, 28), "Unlock ALL"))
        {
            SafeRun(() =>
            {
                NpcCatalog.UnlockAll(_filteredNpcs, LoggerInstance);
                RefreshNpcs();
                SetStatus($"Unlocked {_filteredNpcs.Count} NPCs (current filter).");
            });
        }
        if (Btn(new Rect(328, y, 130, 28), "Max ALL rel"))
        {
            SafeRun(() =>
            {
                NpcCatalog.MaxAllRelationships(_filteredNpcs, LoggerInstance);
                RefreshNpcs();
                SetStatus($"Maxed relationships for {_filteredNpcs.Count} NPCs.");
            });
        }
        if (Btn(new Rect(466, y, 110, 28), "Refresh"))
            RefreshNpcs();
        y += 36;

        float fx = 12;
        for (var i = 0; i < NpcFilters.Length; i++)
        {
            if (TabButton(new Rect(fx, y, 90, 26), NpcFilters[i], _npcFilterIndex == i))
            {
                _npcFilterIndex = i;
                _npcPage = 0;
                ApplyNpcFilter();
            }
            fx += 96;
        }
        y += 34;

        const int perPage = 8;
        var start = _npcPage * perPage;
        var page = _filteredNpcs.Skip(start).Take(perPage).ToList();
        for (var i = 0; i < page.Count; i++)
        {
            var n = page[i];
            var rowY = y + i * 36;
            var selected = _selectedNpc?.Id == n.Id;
            var tag = n.IsSupplier ? "SUP" : "NPC";
            var lockTag = n.Unlocked ? "OK" : "LOCK";
            var label = $"{lockTag} [{tag}] {n.Name}  rel {n.Relationship:0.#}/5  [{n.Id}]";
            if (TabButton(new Rect(12, rowY, 430, 30), label, selected))
                _selectedNpc = n;

            if (Btn(new Rect(450, rowY, 80, 30), "Unlock"))
            {
                SafeRun(() =>
                {
                    NpcCatalog.Unlock(n.Id, LoggerInstance);
                    RefreshNpcs();
                    SetStatus($"Unlocked {n.Name}");
                });
            }
            if (Btn(new Rect(538, rowY, 70, 30), "Max"))
            {
                SafeRun(() =>
                {
                    NpcCatalog.UnlockAndMax(n.Id, LoggerInstance);
                    RefreshNpcs();
                    SetStatus($"Unlocked + maxed {n.Name}");
                });
            }
            if (Btn(new Rect(616, rowY, 50, 30), "0"))
            {
                SafeRun(() =>
                {
                    NpcCatalog.SetRelationship(n.Id, 0f, LoggerInstance);
                    RefreshNpcs();
                    SetStatus($"{n.Name} → rel 0");
                });
            }
            if (Btn(new Rect(672, rowY, 50, 30), "3"))
            {
                SafeRun(() =>
                {
                    NpcCatalog.SetRelationship(n.Id, 3f, LoggerInstance);
                    RefreshNpcs();
                    SetStatus($"{n.Name} → rel 3");
                });
            }
            if (Btn(new Rect(728, rowY, 50, 30), "5"))
            {
                SafeRun(() =>
                {
                    NpcCatalog.SetRelationship(n.Id, 5f, LoggerInstance);
                    RefreshNpcs();
                    SetStatus($"{n.Name} → rel 5");
                });
            }
        }

        var pages = Math.Max(1, (int)Math.Ceiling(_filteredNpcs.Count / (double)perPage));
        var by = y + perPage * 36 + 6;
        if (Btn(new Rect(12, by, 70, 26), "< Prev") && _npcPage > 0)
            _npcPage--;
        GUI.Label(new Rect(90, by, 360, 26),
            $"Page {_npcPage + 1}/{pages}  ({_filteredNpcs.Count} shown / {_npcs.Count} total)", _labelStyle);
        if (Btn(new Rect(460, by, 70, 26), "Next >") && _npcPage < pages - 1)
            _npcPage++;

        if (_selectedNpc != null)
        {
            GUI.Label(new Rect(550, by, 220, 26),
                $"Sel: {_selectedNpc.Name}", _labelStyle);
        }
    }

    private void DrawQuests(float y)
    {
        GUI.Label(new Rect(12, y, 500, 22), "Complete quests (live game list + fallbacks)", _headerStyle);
        y += 28;

        if (Btn(new Rect(12, y, 220, 30), "Complete ALL listed"))
        {
            SafeRun(() =>
            {
                QuestCatalog.CompleteAll(_quests, LoggerInstance);
                RefreshQuests();
                SetStatus($"Tried completing {_quests.Count} quests.");
            });
        }
        if (Btn(new Rect(240, y, 140, 30), "Refresh quests"))
            RefreshQuests();
        y += 40;

        const int perPage = 12;
        var start = _questPage * perPage;
        var page = _quests.Skip(start).Take(perPage).ToList();
        for (var i = 0; i < page.Count; i++)
        {
            var q = page[i];
            var label = string.IsNullOrEmpty(q.State)
                ? $"Complete: {q.Title}"
                : $"Complete: {q.Title}  [{q.State}]";
            if (Btn(new Rect(12, y + i * 32, 740, 28), label))
            {
                SafeRun(() =>
                {
                    QuestCatalog.Complete(q, LoggerInstance);
                    RefreshQuests();
                    SetStatus($"Completed: {q.Title}");
                });
            }
        }

        var pages = Math.Max(1, (int)Math.Ceiling(_quests.Count / (double)perPage));
        var by = y + perPage * 32 + 8;
        if (Btn(new Rect(12, by, 70, 26), "< Prev") && _questPage > 0)
            _questPage--;
        GUI.Label(new Rect(90, by, 280, 26), $"Page {_questPage + 1}/{pages}  ({_quests.Count} quests)", _labelStyle);
        if (Btn(new Rect(380, by, 70, 26), "Next >") && _questPage < pages - 1)
            _questPage++;
    }

    private void DrawWorld(float y)
    {
        GUI.Label(new Rect(12, y, 400, 22), "World / utilities", _headerStyle);
        y += 30;

        if (Btn(new Rect(12, y, 140, 32), "Save game"))
            SafeRun(() => { ConsoleHelper.SaveGame(); SetStatus("Saved."); });
        if (Btn(new Rect(160, y, 140, 32), "Grow plants"))
            SafeRun(() => { ConsoleHelper.GrowPlants(); SetStatus("Plants grown."); });
        if (Btn(new Rect(308, y, 140, 32), "Clear trash"))
            SafeRun(() => { ConsoleHelper.ClearTrash(); SetStatus("Trash cleared."); });
        if (Btn(new Rect(456, y, 140, 32), "Freecam"))
            SafeRun(() => { ConsoleHelper.Submit("freecam"); SetStatus("Freecam toggled."); });
        y += 42;

        if (Btn(new Rect(12, y, 140, 32), "Law intensity 0"))
            SafeRun(() => { ConsoleHelper.SetLawIntensity(0f); SetStatus("Law → 0"); });
        if (Btn(new Rect(160, y, 100, 32), "Noon"))
            SafeRun(() => { ConsoleHelper.SetTime("1200"); SetStatus("Time → 12:00"); });
        if (Btn(new Rect(270, y, 100, 32), "Evening"))
            SafeRun(() => { ConsoleHelper.SetTime("1800"); SetStatus("Time → 18:00"); });
        if (Btn(new Rect(380, y, 100, 32), "Night"))
            SafeRun(() => { ConsoleHelper.SetTime("2200"); SetStatus("Time → 22:00"); });
        y += 46;

        GUI.Label(new Rect(12, y, 500, 22), $"Time scale  (current {_timeScale:0.##}x)", _headerStyle);
        y += 26;
        float sx = 12;
        foreach (var scale in TimeScalePresets)
        {
            var label = scale <= 0.001f ? "Pause" : $"{scale:0.##}x";
            if (TabButton(new Rect(sx, y, 88, 30), label, Math.Abs(_timeScale - scale) < 0.001f))
                SafeRun(() => SetTimeScale(scale));
            sx += 96;
        }
        y += 44;

        GUI.Label(new Rect(12, y, 300, 22), "Spawn vehicle", _headerStyle);
        y += 26;
        float x = 12;
        foreach (var v in new[] { "shitbox", "vespa", "bruiser", "dune", "hounddog", "cheetah" })
        {
            if (Btn(new Rect(x, y, 110, 28), v))
                SafeRun(() => { ConsoleHelper.SpawnVehicle(v); SetStatus($"Spawned {v}"); });
            x += 118;
        }
    }

    private void SetTimeScale(float scale)
    {
        _timeScale = scale;
        var arg = scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try { ConsoleHelper.Submit($"settimescale {arg}"); }
        catch (Exception ex) { LoggerInstance.Warning($"settimescale submit failed: {ex.Message}"); }
        try { Time.timeScale = scale; }
        catch (Exception ex) { LoggerInstance.Warning($"Time.timeScale failed: {ex.Message}"); }
        SetStatus(scale <= 0.001f ? "Time paused (0x)." : $"Time scale → {scale:0.##}x");
    }

    private void RefreshUnlockables()
    {
        _unlockables = new List<(string, string, bool, Action)>();

        try
        {
            foreach (var p in PropertyManager.GetAllProperties())
            {
                if (p == null || string.IsNullOrEmpty(p.PropertyCode))
                    continue;
                var code = p.PropertyCode;
                var name = string.IsNullOrEmpty(p.PropertyName) ? code : p.PropertyName;
                var owned = p.IsOwned;
                var prop = p;
                _unlockables.Add((name, code, owned, () =>
                {
                    prop.SetOwned();
                    ConsoleHelper.Submit($"setowned {code}");
                }));
            }
        }
        catch (Exception ex)
        {
            LoggerInstance.Warning($"Property list failed: {ex.Message}");
        }

        try
        {
            foreach (var b in BusinessManager.GetAllBusinesses())
            {
                if (b == null || string.IsNullOrEmpty(b.PropertyCode))
                    continue;
                var code = b.PropertyCode;
                var name = string.IsNullOrEmpty(b.PropertyName) ? code : b.PropertyName;
                var owned = b.IsOwned;
                var biz = b;
                // Avoid duplicates if already listed as property
                if (_unlockables.Any(u => u.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _unlockables.Add((name + " (biz)", code, owned, () =>
                {
                    biz.SetOwned();
                    ConsoleHelper.Submit($"setowned {code}");
                }));
            }
        }
        catch (Exception ex)
        {
            LoggerInstance.Warning($"Business list failed: {ex.Message}");
        }

        // Hardcoded fallbacks if registry empty
        if (_unlockables.Count == 0)
        {
            foreach (var code in new[]
                     {
                         "motelroom", "sweatshop", "seweroffice", "storageunit", "bungalow", "barn",
                         "dockswarehouse", "manor", "laundromat"
                     })
            {
                var c = code;
                _unlockables.Add((c, c, false, () => ConsoleHelper.Submit($"setowned {c}")));
            }
        }

        _unlockables = _unlockables
            .OrderBy(u => u.Owned)
            .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void GiveSelected(int qty)
    {
        if (_selectedItem == null)
        {
            SetStatus("No item selected.");
            return;
        }
        SafeRun(() =>
        {
            ItemCatalog.Give(_selectedItem.Id, qty, LoggerInstance);
            SetStatus($"Gave {qty}× {_selectedItem.Name} ({_selectedItem.Id})");
        });
    }

    private void RefreshItemCache(bool force = false)
    {
        if (!force && _allItems.Count > 0)
        {
            ApplyFilter();
            return;
        }

        try
        {
            _allItems = ItemCatalog.Build(LoggerInstance);
            ApplyFilter();
            SetStatus($"Loaded {_allItems.Count} items (full catalog).");
        }
        catch (Exception ex)
        {
            _allItems = new List<ItemEntry>();
            _filteredItems = new List<ItemEntry>();
            SetStatus($"Items unavailable — load a save. ({ex.Message})");
            LoggerInstance.Error($"Item catalog failed: {ex}");
        }
    }

    private void RefreshQuests()
    {
        try
        {
            _quests = QuestCatalog.Build(LoggerInstance);
            var pages = Math.Max(1, (int)Math.Ceiling(_quests.Count / 12.0));
            if (_questPage >= pages)
                _questPage = pages - 1;
        }
        catch (Exception ex)
        {
            _quests = new List<QuestEntry>();
            LoggerInstance.Error($"Quest catalog failed: {ex}");
        }
    }

    private void RefreshNpcs()
    {
        try
        {
            _npcs = NpcCatalog.Build(LoggerInstance);
            ApplyNpcFilter();
            if (_selectedNpc != null)
                _selectedNpc = _npcs.FirstOrDefault(n =>
                    n.Id.Equals(_selectedNpc.Id, StringComparison.OrdinalIgnoreCase));
            SetStatus($"Loaded {_npcs.Count} NPCs ({_npcs.Count(n => n.IsSupplier)} suppliers).");
        }
        catch (Exception ex)
        {
            _npcs = new List<NpcEntry>();
            _filteredNpcs = new List<NpcEntry>();
            LoggerInstance.Error($"NPC catalog failed: {ex}");
            SetStatus($"NPC list failed: {ex.Message}");
        }
    }

    private void ApplyNpcFilter()
    {
        var filter = NpcFilters[Math.Clamp(_npcFilterIndex, 0, NpcFilters.Length - 1)];
        IEnumerable<NpcEntry> q = _npcs;
        if (filter == "Suppliers")
            q = q.Where(n => n.IsSupplier);
        else if (filter == "Locked")
            q = q.Where(n => !n.Unlocked);
        else if (filter == "Unlocked")
            q = q.Where(n => n.Unlocked);

        _filteredNpcs = q.ToList();
        var pages = Math.Max(1, (int)Math.Ceiling(_filteredNpcs.Count / 8.0));
        if (_npcPage >= pages)
            _npcPage = pages - 1;
    }

    private void DrawSearchField(Rect rect)
    {
        // GUI.DrawTexture is stripped on this IL2CPP build — use a normal button only.
        _caretBlink += Time.deltaTime;
        var caret = _searchFocused && ((_caretBlink % 1f) < 0.5f) ? "|" : "";
        string shown;
        if (string.IsNullOrEmpty(_itemSearch))
            shown = _searchFocused ? $"> type to search{caret}" : "Click here, then type to search...";
        else
            shown = _searchFocused ? _itemSearch + caret : _itemSearch;

        if (TabButton(rect, shown, _searchFocused))
            _searchFocused = true;
    }

    private void ApplyFilter()
    {
        var filter = ItemFilters[Math.Clamp(_filterIndex, 0, ItemFilters.Length - 1)];
        IEnumerable<ItemEntry> q = _allItems;

        if (filter == "A-F")
            q = q.Where(i => StartsInRange(i, 'a', 'f'));
        else if (filter == "G-M")
            q = q.Where(i => StartsInRange(i, 'g', 'm'));
        else if (filter == "N-S")
            q = q.Where(i => StartsInRange(i, 'n', 's'));
        else if (filter == "T-Z")
            q = q.Where(i => StartsInRange(i, 't', 'z'));
        else if (filter != "All")
            q = q.Where(i =>
                (i.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                (i.Id?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);

        var search = (_itemSearch ?? "").Trim();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(i =>
                (i.Name?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                (i.Id?.IndexOf(search, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
        }

        _filteredItems = q.ToList();
        var pages = Math.Max(1, (int)Math.Ceiling(_filteredItems.Count / (double)PageSize));
        if (_itemPage >= pages)
            _itemPage = pages - 1;
    }

    private static bool StartsInRange(ItemEntry item, char from, char to)
    {
        var s = !string.IsNullOrEmpty(item.Name) ? item.Name : item.Id;
        if (string.IsNullOrEmpty(s))
            return false;
        var c = char.ToLowerInvariant(s[0]);
        return c >= from && c <= to;
    }

    private void SafeRun(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}");
            LoggerInstance.Error($"Action failed: {ex}");
        }
    }

    private void SetStatus(string message)
    {
        _status = message;
        LoggerInstance.Msg(message);
    }

    private static bool Btn(Rect r, string label) => GUI.Button(r, label, _btnStyle);

    private static bool TabButton(Rect r, string label, bool active)
    {
        var prev = GUI.backgroundColor;
        if (active)
            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.55f);
        var clicked = GUI.Button(r, label, _btnStyle);
        GUI.backgroundColor = prev;
        return clicked;
    }

    private static void EnsureStyles()
    {
        if (_stylesReady && _btnStyle != null)
            return;

        _panelBg = MakeTex(new Color(0.07f, 0.09f, 0.11f, 0.95f));
        _btnBg = MakeTex(new Color(0.18f, 0.22f, 0.27f, 1f));
        _btnHover = MakeTex(new Color(0.28f, 0.4f, 0.5f, 1f));
        _btnActive = MakeTex(new Color(0.25f, 0.55f, 0.4f, 1f));
        _fieldBg = MakeTex(new Color(0.12f, 0.14f, 0.17f, 1f));
        _fieldFocusBg = MakeTex(new Color(0.16f, 0.28f, 0.22f, 1f));

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = Color.white },
            clipping = TextClipping.Clip
        };
        _headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.65f, 1f, 0.78f) }
        };
        _statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            wordWrap = true,
            normal = { textColor = new Color(0.8f, 0.85f, 0.9f) }
        };
        _btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = _btnBg, textColor = Color.white },
            hover = { background = _btnHover, textColor = Color.white },
            active = { background = _btnActive, textColor = Color.white },
            padding = new RectOffset(6, 6, 4, 4)
        };
        _fieldStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = Color.white }
        };

        GUI.skin.window.normal.background = _panelBg;
        GUI.skin.window.onNormal.background = _panelBg;
        _stylesReady = true;
    }

    private static Texture2D MakeTex(Color color)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.SetPixels(new[] { color, color, color, color });
        tex.Apply();
        return tex;
    }
}
