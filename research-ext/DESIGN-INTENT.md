# Schedule I — Design Intent for the Three Cut Roadmap Features

> Research date: **2026-08-03**. Game version at time of writing: **v0.4.6 "Gamepad Support + Optimization"**, released **2026-08-01**.
> Every claim below is tagged **[verified]** (primary: Trello / Steam announcement / official), **[community]** (wiki, guide, Reddit, decompiled mod source), or **[inference]** (my reasoning).
> Raw captures live in `research-ext/raw/`: `trello-board.json`, `trello-cards-dump.txt`, `steam-news-raw.json`, `steam-news-all.txt`, `steam-news-clean.txt`, `fandom-*.json`.

---

## 0. Executive summary — read this first

The single most important finding: **the exact card wording in the user's screenshot is not from Trello at all.** It is verbatim from **Community Vote #3**, the in-game main-menu voting panel, announced on Steam on **2026-04-17** by Tyler. All three "cut roadmap features" were the three ballot options in that vote.

| Finding | Detail |
|---|---|
| Primary source found? | **Yes.** Official Trello board `trello.com/b/VQQpru3F` is live (last board activity **2026-07-13**) and the exact three-feature wording is in the Steam announcement dated **2026-04-17**. |
| Vote #3 result | **Special Customers won: 142,287 votes.** Hireable Drivers 2nd: **125,314**. Police Improvements 3rd: **117,677**. [verified] |
| Special Customers status | **Not shipped. In active development.** It sits in the Trello **"In Progress"** list and is slated as **v0.5.0**. The v0.4.6 notes (2026-08-01) say it is "not far off now" with a customer-group art sneak-peek. |
| Hireable Drivers status | **Not shipped. Lost two votes** (Vote #2 in Sept 2025 and Vote #3 in Apr 2026). Sits in Trello **"Future Update Ideas"**. |
| Police Improvements status | **Not shipped. Lost two votes** (Vote #1 in June 2025 as "Police Expansion Update", Vote #3 in Apr 2026). Two separate Trello cards exist for it, both in **"Future Update Ideas"**. |
| Partially shipped? | **Police only, and only incidentally.** No dynamic intensity, no federal agents, no outlaw status. What did land is a slow drip of AI/behaviour fixes plus one real scaling change in v0.4.6 (see §10.6). |
| Biggest design gift | Vote #2 (2025-09-01) contains a **much more detailed Drivers spec** than Vote #3 — it names the exact mechanic: player-designated automatic transit routes with numeric trigger conditions. |

The mod work is therefore **three genuinely unshipped features**, with one caveat: **Special Customers will ship, probably within weeks.** Design that mod so it can coexist with or be superseded by v0.5.0.

---

## 1. Source correction — where the exact wording comes from

The user's screenshot wording matches, word for word, the Steam announcement **"Community vote #3 is now open!"** (Tyler, 2026-04-17). Full verbatim text [verified]:

> "Hi everyone,
> The third community vote is now open! In the main menu, you'll be able to vote on what you want to see in the next major update. This time, the three options are:
>
> **Hireable Drivers**
> Driver employees who can transport items between your properties, businesses, and dealers.
>
> **Police Improvements**
> Dynamic police intensity, federal agents, outlaw status, and heavier consequences when caught.
>
> **Special Customers**
> Special customer groups (bikers, hippies, businessmen, etc.) who periodically visit town and buy large quantities of product.
>
> The voting period will run for one week, and we'll announce the results shortly after that. You'll also be able to view the results in the main menu.
> Thanks for reading,
> Tyler"

— <https://store.steampowered.com/news/app/3164500/view/1830163047259398>, 2026-04-17. [verified]

Supporting context: patch **v0.4.5f2** (2026-04-17) shipped a single line — *"Added choice descriptions to the community vote panel."* — which is literally the patch that put those three descriptions on screen. <https://store.steampowered.com/news/app/3164500/view/1830163047259391> [verified]

**Consequence for us:** the ballot descriptions are *marketing-length summaries*, not specs. The real mechanical detail is elsewhere: the Vote #2 announcement (Drivers) and the pre-2026 Trello card descriptions as quoted by PC Gamer in April 2025. Both are captured below.

---

## 2. The official Trello board — structure and full card list

**Board:** "Schedule I Roadmap" — <https://trello.com/b/VQQpru3F/schedule-i-roadmap>
**Member:** Tyler (`tyler_tvgs`) — sole member. **Board last activity:** 2026-07-13T00:41:50Z. [verified, fetched via `trello.com/b/VQQpru3F.json` on 2026-08-03]

**Important caveat:** the board's card descriptions have been **stripped or encrypted** in the current public export. Every visible card now has an empty `desc` field, and the 28 archived cards return base64/encrypted blobs for both name and description. The rich descriptions that journalists quoted in early 2025 are **no longer publicly readable**. Wayback Machine snapshots exist (2025-03-16 through 2026-05-11) but Trello is a client-rendered SPA, so the archived HTML contains no card data. **The April 2025 press quotes in §3–§5 are therefore the best surviving record of the original card text.** [verified]

### Lists (4)

| Position | List | Cards |
|---|---|---|
| 1 | **In Progress** | 3 |
| 2 | **Future Update Ideas** | 14 |
| 3 | **Smaller Additions/Improvements** | 6 |
| 4 | **Done** | 20 |

Total open cards: **43**. Archived: **28**. Board total: **71**. [verified]

### Full card list (live board, 2026-08-03)

**In Progress**

| Card | Last activity | Card URL |
|---|---|---|
| Linux release/Steam Deck certification | 2026-07-11 | <https://trello.com/c/ZcVeqsi7> |
| **Special customers** | **2026-04-24** | <https://trello.com/c/hxaS1kcq> |
| Controller support | 2026-03-20 | <https://trello.com/c/ddUsIjkF> |

**Future Update Ideas**

| Card | Last activity | Card URL |
|---|---|---|
| **More police abilities and interaction** | 2026-07-11 | <https://trello.com/c/6dDYjf1z> |
| **Police expansion** | 2026-07-13 | <https://trello.com/c/1eSfUIFM> |
| Property 'heat' and raids | 2026-03-20 | <https://trello.com/c/IwgSiGMh> |
| Fishing | 2026-03-20 | <https://trello.com/c/oRWcm1dz> |
| Property customisation/renovation | 2026-03-20 | <https://trello.com/c/O7P75IXw> |
| MDMA | 2026-03-20 | <https://trello.com/c/QmmlCTSJ> |
| Heroin | 2026-03-20 | <https://trello.com/c/JB9PFrNB> |
| Localisation | 2026-03-20 | <https://trello.com/c/tPNHHTvP> |
| **Hireable drivers** | 2026-07-11 | <https://trello.com/c/5i4t7UQX> |
| South-west map expansion | 2026-07-11 | <https://trello.com/c/CBq3237G> |
| North-east map expansion | 2026-07-11 | <https://trello.com/c/QzRY2aMp> |
| Visual upgrade | 2026-07-11 | <https://trello.com/c/vk7gcscu> |
| Offshore sales | 2026-07-11 | <https://trello.com/c/4XqwnMXg> |
| More environmental interaction/destruction | 2026-07-11 | <https://trello.com/c/2V3qA9i0> |

**Smaller Additions/Improvements**

| Card | Description (only card on the board that still has one) |
|---|---|
| Backpacks, duffel bags, fannypacks | *(empty)* |
| Machine pistol | *(empty)* |
| Product manager search/filter/sort | *(empty)* |
| Plastic surgeon | *(empty)* |
| Vacuum cleaner | *(empty)* |
| Skateboarding improvements | `- Grinds`<br>`- Improved controls/camera on steep angles` |

**Done** (20) — Cartel update · Shrooms · Sewers · Anniversary update · Feedback system upgrade · Graffiti · Item slot filters · Weather · Jukebox · Consolidate save files · Offroad skateboard · **Replace beds with lockers** · Save file import/export · **In-game voting system** · Van · Wall-mounted objects · Bleuball boutique · Pawn shop · Multiplayer bandwidth issues · **Polling system upgrade**

**Notable archived cards** (names still readable): Gun range interior/shooting minigame, Parkour, **Hireable drivers** (a *duplicate*, archived — `trello.com/c/YYpsedBS`, encrypted desc), Custom packaging and labels, Steam Deck certification, Jukebox/radio, Test Card.

**What the landscape tells us** [inference]: Tyler ships player-facing systems and QoL reliably (20 Done cards, ~monthly cadence). The three features we're rebuilding are not abandoned — Drivers and Police are both *still* in Future Update Ideas with 2026-07 activity, meaning he touched them three weeks ago. They lost popularity contests, not design reviews. That is the strongest possible mandate for making our mods feel like they belong: **these are features the developer still intends to build.**

---

## 3. Feature 1 — Hireable Drivers

### 3.1 Exact quoted wording

**Source A — Community Vote #3 ballot** (2026-04-17) [verified]
> **Hireable Drivers**
> "Driver employees who can transport items between your properties, businesses, and dealers."

<https://store.steampowered.com/news/app/3164500/view/1830163047259398>

**Source B — Community Vote #2 ballot** (2025-09-01) — *far more detailed, this is the real spec* [verified]
> **Hireable Drivers**
> "This new employee can transport items between your properties, businesses, and dealers. **You'll be able to designate automatic transit routes and specify the required conditions (e.g. number of items in the vehicle) for a route to begin.** This update will also include some QOL improvements to the delivery system."

<https://store.steampowered.com/news/app/3164500/view/1809235871707524>, 2025-09-01

**Source C — PC Gamer reading the original Trello card** (2025-04-01) [verified — journalist paraphrase of card text]
> "There are also plans for additional distribution options, like hireable drivers that can move your product from your properties to dealers **or collect cash and bring them to your businesses to be laundered.**"

<https://www.pcgamer.com/games/sim/schedule-1-roadmap-future-plans-for-the-drug-dealing-sim-include-a-classic-fishing-minigame-plus-parkour-and-heroin/>, 2025-04-01

**Source D — PCGamesN roadmap list** (card title as of 2025) [community]
> "Hireable drivers **and logistics workers**"

<https://www.pcgamesn.com/schedule-1/roadmap>

### 3.2 Vote history

| Vote | Date | Drivers' result | Winner |
|---|---|---|---|
| #2 | 2025-09-08 | **2nd — 139,000 votes** | Shrooms (269,000+); Fishing 3rd (85,000) |
| #3 | 2026-04-26 | **2nd — 125,314 votes** | Special Customers (142,287); Police 3rd (117,677) |

Drivers is the most consistently *second-place* feature in the game's history — it has never won, and never come last. [verified]

### 3.3 What we can infer about intended mechanics

**Stated outright** [verified]:
1. It is an **employee type**, not a vehicle upgrade or a phone app. It therefore inherits the whole shipped employee contract: hire from Manny, signing fee + daily wage, assigned to a locker on a property, consumes a property employee slot, stops working at 4 AM.
2. Endpoints are **properties, businesses, and dealers** — three distinct destination classes. "Businesses" is significant: businesses currently *cannot have employees assigned* and have no loading docks, so a driver reaching a business is a genuinely new capability.
3. **Player-designated automatic transit routes.**
4. **Numeric trigger conditions** on routes, with the dev's own example being *"number of items in the vehicle"*.
5. It ships alongside **QoL improvements to the delivery system**.
6. A **cash-collection leg** exists: dealer → business, for laundering (Source C).

**Strong inference** [inference]:
- This is explicitly modelled on the **existing Handler route system**. Handlers already have up to **5 logistics routes**, each with exactly one source and one destination, moving items between "almost any type of building with item-slots… and even delivery parking slots". The Driver is the Handler with the property boundary removed and a vehicle attached. Our mod should reuse that UI grammar (clipboard → pick source → pick destination) or it will feel foreign.
- "Number of items **in the vehicle**" implies the driver **accumulates** a load before departing, rather than doing one item per trip. Note that Handlers, despite 5 inventory slots, "are only capable of moving a single stack of a single type of item for a single logistics route per trip" — the driver's whole point is to beat that.
- Vehicle cargo capacity is the natural capacity stat, because it already exists per-vehicle (4–16 slots) and is already balanced against vehicle price.
- The "QoL improvements to the delivery system" line suggests Tyler saw drivers and the $200-fee delivery van system as one problem space. A driver is essentially a *player-owned* delivery van.

**Explicitly unknown** [inference]: whether the driver owns a dedicated vehicle or drives one you buy; whether routes are time-scheduled as well as condition-triggered; whether drivers can be robbed/arrested (dealers currently cannot be — see §8).

---

## 4. Feature 2 — Police Improvements

### 4.1 Exact quoted wording

**Source A — Community Vote #3 ballot** (2026-04-17) [verified]
> **Police Improvements**
> "Dynamic police intensity, federal agents, outlaw status, and heavier consequences when caught."

<https://store.steampowered.com/news/app/3164500/view/1830163047259398>

**Source B — Community Vote #1 ballot** (2025-06-10) [verified] — the same feature, earlier name, *no description given*:
> "For this vote, the three options are:
> - Rival Cartel Update
> - **Police Expansion Update**
> - Shrooms Update"

<https://store.steampowered.com/news/app/3164500/view/1801617199549230>

**Source C — PC Gamer reading the original Trello card** (2025-04-01) [verified — journalist quoting card text]
> "Other ideas on the roadmap include giving the cops more interactions: **police bribes, raids on your properties, and an evidence room where your confiscated items go when you've been arrested. 'You can break in to try and recover your stuff.'**"

<https://www.pcgamer.com/games/sim/schedule-1-roadmap-future-plans-for-the-drug-dealing-sim-include-a-classic-fishing-minigame-plus-parkour-and-heroin/>, 2025-04-01

**Source D — mein-mmo roadmap list** (2025) [community] — independent read of the same card set:
> "More interactions with the police (**bribery**)" … "Police **evidence locker** that you can break into to get back your seized goods"

<https://mein-mmo.de/en/schedule-1-and-its-roadmap-all-content-you-can-expect-in-the-future,1245998/>

**Source E — PCGamesN roadmap list** [community]: "More police interactions (including bribery)"; separately "**Property 'heat' and raids**" — which is still a live, separate Trello card today.

### 4.2 Vote history

| Vote | Date | Police result | Winner |
|---|---|---|---|
| #1 | 2025-06-16 | **Lost.** 440,000+ total submissions, **64% chose Rival Cartel** | Rival Cartel |
| #3 | 2026-04-26 | **3rd — 117,677 votes** ("a close third") | Special Customers |

Tyler's own framing after Vote #1 [verified]: *"Don't worry if your preferred choice wasn't selected; non-winning choices will be available again in a future community vote!"* — and indeed Police reappeared on Vote #3. <https://store.steampowered.com/news/app/3164500/view/1802354289676065>

### 4.3 Did any of it ship? — partial, and less than it looks

**No.** None of the four named pillars shipped. There is **no heat/intensity meter, no federal agents, no outlaw status, and no increase to arrest penalties** in v0.4.6. What did land is incremental AI and behaviour work spread across patches, plus **one genuine scaling change**:

| Version | Date | Police-related change | Relevance |
|---|---|---|---|
| v0.3.6-era | 2025 | "Police now notice pickpocketing"; "Added a slight (15-minute) tolerance to police when enforcing curfew"; "Added curfew 30-minute warning" | Curfew softening, not hardening |
| v0.4.0f8 | 2025-09-27 | "Police checkpoints are now lowered if there are no officers manning them"; "Tweaked the NPC ranged weapon hit chance algorithm"; "Tweaked various vision system thresholds and durations"; "Tweaked combat/pursuit behaviour" | Fidelity, not intensity |
| v0.4.1 | 2025-11-02 | Sewers added — *"a new way to navigate Hyland Point without having to worry about police and cartel members"* | Police **evasion** got easier |
| v0.4.4 | 2026-03-19 | "Added more police sentry locations"; "Police will now alternate between two locations during their sentry behaviour"; "Police will no longer tase you if you're within arrest distance" | More posts, gentler tasing |
| **v0.4.6** | **2026-08-01** | **"Police patrols, sentries, checkpoints now assign 1-2 officers instead of 2 all the time."** Also: "Added new player 'visibility points' at the neck and hips to prevent cheesing NPC vision"; "Police vehicles now pooled instead of spawned/destroyed" | **This is the closest thing to "dynamic intensity" that exists.** It introduces a *variable officer count per post* — exactly the hook a heat system should drive. |

Sources: <https://store.steampowered.com/news/app/3164500/view/1839676055889605> (v0.4.6), <https://store.steampowered.com/news/app/3164500/view/1811772772305948> (v0.4.0f8), <https://store.steampowered.com/news/app/3164500/view/1827626365752067> (v0.4.4). [verified]

**Key implication for our mod** [inference]: v0.4.6 already varies officers-per-post between 1 and 2. A heat system should *widen that band* (0–4) rather than invent a parallel spawner. That is the single most native-feeling hook available.

### 4.4 What we can infer about intended mechanics

**Stated outright** [verified]: dynamic police **intensity** (a scalar that moves, not a fixed difficulty); **federal agents** (a distinct, presumably higher-tier NPC class); **outlaw status** (a persistent player *state*, not a momentary wanted level); **heavier consequences when caught** (the arrest penalty is being explicitly called out as too light).

**From the older Trello card** [verified via press]: **bribery**, **property raids**, and a **police evidence room** you can burgle to recover confiscated goods.

**Strong inference** [inference]:
- "Intensity" is separate from the existing **wanted level**, which is momentary and *fully cleared by sleeping* ("Wanted level is now cleared when you sleep" — v0.1.x patch note). Intensity must therefore be **persistent across days**, or it is just the wanted level again.
- The game already scales police by **rank**: "As you level up… only a few police officers will patrol… Eventually, you'll see police SUV's and there will be fences on either side of checkpoints." Intensity is presumably the *dynamic* sibling of that *static* rank curve — driven by your recent behaviour rather than your XP.
- "Outlaw status" naming implies **a binary/threshold state with visible world consequences**, not a slider. In a game where shops are already time-gated and payment-gated (cash-only vs card-only), the natural expression is *losing access to legitimate services*.
- "Heavier consequences when caught" is a direct response to a known balance hole: current fines are **$5–$150 per charge**, paid in cash only, **with no debt if you can't pay** — i.e. arrest is nearly free if you carry no cash. And vehicle cargo is currently untouchable (vehicles "do not take damage… making them effectively invulnerable, along with the occupants and cargo inside"). Both are obvious targets.

---

## 5. Feature 3 — Special Customers

### 5.1 Exact quoted wording

**Source A — Community Vote #3 ballot** (2026-04-17) [verified]
> **Special Customers**
> "Special customer groups (bikers, hippies, businessmen, etc.) who periodically visit town and buy large quantities of product."

**Source B — Vote #3 results announcement** (2026-04-26) — *the richest design statement of the three features* [verified]
> "Thank you to everyone who participated in the latest community vote! Special customers won with 142,287 votes. Drivers came in second at 125,314 votes, and police improvements was a close third at 117,677.
> This update will introduce several travelling customer groups, such as:
> - Bikers
> - Businessmen
> - Hippies
> - Party bus
> - Rock band
>
> **Each group will have one or two preferred drug types, which they will purchase in large quantities whenever they're in town. This will provide a fast method of offloading bulk quantities of product, albeit at a slightly lower profit margin. I think this will be a great addition and will add a lot more 'rhythm' to the gameplay.**
> This will be quite an art-heavy update, so we're expecting it to land in June-July. At the moment, we're working on gamepad support + Steam Deck certification, which should be done in about a month. Development for special customers will likely start in about 2 weeks.
> Thanks for reading, Tyler"

<https://store.steampowered.com/news/app/3164500/view/1830797770239647>

**Source C — PC Gamer quoting the original Trello card verbatim** (2025-04-01) [verified]
> "**'Customers or groups of customers periodically visit Hyland Point,'** the roadmap says. **'While they're in town, they'll buy as much as you can sell of their preferred drug type.'**
> Examples given are **a biker gang coming to the city, which would be an opportunity to sell meth in bulk, or businesspeople visiting which would allow you to unload a bunch of cocaine.**"

<https://www.pcgamer.com/games/sim/schedule-1-roadmap-future-plans-for-the-drug-dealing-sim-include-a-classic-fishing-minigame-plus-parkour-and-heroin/>, 2025-04-01

**Source D — PCGamesN roadmap list** [community]: card title on the 2025 board was **"Travelling customers"**.

**Source E — vote share** [community]: 142,287 votes = **36.9%** of the ballot, implying ~385,600 total votes cast. <https://beefsuplex.com/news/schedule-one-community-vote-three/>, 2026-04-26.

### 5.2 Current status — IN PROGRESS, ships imminently

- Trello: card **"Special customers"** is in the **In Progress** list, last touched **2026-04-24**. [verified]
- v0.4.6 announcement (2026-08-01) [verified]:
  > "**Special Customer Update** — We planned to release gamepad support at the end of June, and release special customers in mid-late July. Gamepad support took a lot longer than expected, but we've been working on the special customers update in parallel, so it's not far off now. It's shaping up really nicely, and I think it'll be well worth the wait! Here's a sneak-peek of one of the customer groups: [image]"
  <https://store.steampowered.com/news/app/3164500/view/1839676055889605>
- Fandom's Updates table already reserves the slot: **"v0.5.0: Special Customers Update — 2026.08.??"**. [community] <https://schedule-1.fandom.com/wiki/Updates>

**Recommendation** [inference]: build this mod with a hard kill-switch and a config flag, and namespace all IDs. When v0.5.0 lands, our version should either disable itself or be repositioned as an *extension* (extra groups, tunable cadence) rather than a replacement.

### 5.3 What we can infer about intended mechanics

**Stated outright** [verified]:
1. **Five named groups**: Bikers, Businessmen, Hippies, Party bus, Rock band.
2. Each group has **one or two preferred drug types** — not the shipped 4-way affinity vector, a narrower preference.
3. They **travel** — they arrive, stay a while, leave. Periodic, not permanent.
4. They buy **in large quantities**, and the pitch is **"a fast method of offloading bulk quantities of product."**
5. **"albeit at a slightly lower profit margin"** — the explicit balance lever. Bulk convenience is paid for in margin.
6. Design goal is stated as **"a lot more 'rhythm' to the gameplay."**
7. From the older card: **"they'll buy as much as you can sell of their preferred drug type"** — suggesting the constraint is *your supply*, not their budget.
8. Group→drug examples from the card: **Bikers → meth**, **Businesspeople → cocaine**.
9. It is **"quite an art-heavy update"** — i.e. the cost is new NPC models/vehicles, implying groups are **physically present in the world** (a party *bus*, a biker gang) rather than a phone-only transaction.

**Strong inference** [inference]:
- "Rhythm" + "periodically visit" ⇒ a **recurring beat measured in in-game days**. The shipped customer system already runs on a weekly order rhythm (1–7 orders/week, each customer with a `Preferred Order Day`), so a group cadence should be expressed in the same units.
- "Slightly lower profit margin" is unambiguous once you have the shipped appeal formula (§9.4): it means the group's acceptable **price / market value ratio** is *lower* than a normal customer's. It does **not** mean smaller orders — orders are explicitly larger.
- "Buy as much as you can sell" + "slightly lower margin" together imply the group's budget is effectively **not the binding constraint**; it is designed as an **inventory sink** for players drowning in product.
- Hippies → weed and/or shrooms; Rock band → cocaine and/or shrooms; Party bus → a party-drug mix. These three mappings are **not sourced** — only Bikers→meth and Businessmen→cocaine are.

---

## 6. The community-vote landscape (full context)

Three votes have run. The polling system itself is a shipped feature ("In-game voting system" and "Polling system upgrade" are both on the Done list).

| Vote | Opened | Closed/announced | Options | Result |
|---|---|---|---|---|
| **#1** | 2025-06-10 | 2025-06-16 | Rival Cartel Update · **Police Expansion Update** · Shrooms Update | **Rival Cartel — 64% of 440,000+ submissions** |
| **#2** | 2025-09-01 | 2025-09-08 | **Hireable Drivers** · Fishing · Shrooms | **Shrooms — 269,000+**; Drivers 139,000; Fishing 85,000 |
| **#3** | 2026-04-17 | 2026-04-26 | **Hireable Drivers** · **Police Improvements** · **Special Customers** | **Special Customers — 142,287**; Drivers 125,314; Police 117,677 |

Shipped-update timeline for calibration [verified, Steam news feed for app 3164500]:

| Version | Date | Name |
|---|---|---|
| v0.1.14–v0.2.9 | 2024-12 → 2025-02 | Free Sample era (multi-drag, pickpocketing/counter-offers, co-op, customer improvements, Packaging/Mixing Mk II) |
| — | **2025-03-24** | **Steam Early Access launch** (peaked 459k concurrent) |
| v0.3.4 | 2025-04-10 | Pawn Shop, Fancy Stuff |
| v0.3.5 | 2025-05-08 | Jukebox, Storage Unit |
| v0.3.6 | 2025-06-08 | Filters, Employee Tweaks, Save System — **beds → lockers** |
| v0.4.0 | 2025-08-27 | **Rival Cartel** (vote #1 winner) |
| v0.4.1 | 2025-11-02 | Halloween — sewers, climbing |
| v0.4.2 | 2025-12-26 | **Shrooms** (vote #2 winner) |
| v0.4.3 | 2026-02-02 | Storage Closets; **dealer customer cap 8 → 10** |
| v0.4.4 | 2026-03-19 | Weather |
| v0.4.5 | 2026-03-30 | Anniversary |
| **v0.4.6** | **2026-08-01** | **Gamepad Support + Optimization** ← current |
| v0.5.0 | expected 2026-08 | **Special Customers** (vote #3 winner) |

Early Access is planned to run **~2 years from March 2025** (i.e. to ~March 2027), and the price is stated to rise 25–50% at 1.0. [community — <https://www.pcgamesn.com/schedule-1/roadmap>, <https://dotesports.com/indies/news/schedule-1-roadmap>]

---

# Part 2 — Shipped systems: behaviour and balance numbers

> **Patch-drift warning.** Every number below carries a date. Values marked *(v0.4.x era)* were verified against the Fandom wiki as edited between 2026-04 and 2026-07 and cross-checked against Steam patch notes. Values from March–April 2025 guides are flagged as such and should be treated as *possibly stale*.

## 7. Employees

Hired from **Manny** in the **Warehouse** (open 6 PM – 6 AM; unlocked at rank Hoodlum V). Payment is **cash-only**. [community]

### 7.1 Types, costs, capacity

| Employee | Signing fee | Daily wage | Assignable to | Per-employee limit |
|---|---|---|---|---|
| **Cleaner** | $500–$1,000 | **$100/day** | Trash cans | **6 trash cans** (raised from 3 in v0.4.2, 2025-12-26) |
| **Botanist** | $1,000–$1,500 | **$200/day** | Supply storage, Grow Tent, Plastic/Moisture-Preserving/Air Pot, Drying Rack, Mushroom Bed, Mushroom Spawn Station | **8 pots** |
| **Handler** | **$1,000** (+$100 per existing employee) | **$200/day** | Packaging Station, Packaging Station Mk II, Brick Press | **3 machines + 5 logistics routes** |
| **Chemist** | **$2,000** | **$300/day** | Mixing Station, Mixing Station Mk2, Chemistry Station, Lab Oven, Cauldron | **4 stations** (raised from 3, 2025) |

Sources: <https://schedule-1.fandom.com/wiki/Employees> (retrieved 2026-08-03) [community]; <https://schedule-1.fandom.com/wiki/Handlers> (edited 2026-04-08) [community]; <https://www.dexerto.com/gaming/how-to-hire-assign-workers-in-schedule-1-3171655/> (2025, **stale — lists Cleaner sign-on as $1,500 and Handler at 3 stations**) [community].

**Note on fee variance** [inference]: sources disagree on Cleaner/Botanist base fees ($500 vs $1,000 vs $1,500). The Handlers wiki page resolves it: the fee is **base + $100 per employee you already have**. The "ranges" in the Employees table are the same number observed at different employee counts. There are **exactly 4 employee types** — no driver, no "logistics worker", confirming §3 is unshipped.

### 7.2 Payment, hours, and requirements

| Rule | Value | Source |
|---|---|---|
| Payment container | **Locker** (preferred) or **Bed** + briefcase, **on the same property** | Fandom Employees [community] |
| Locker capacity | **6 slots**, max **$6,000** stored; occupies 1×3 tiles | <https://schedule-1.fandom.com/wiki/Locker> [community] |
| Beds → lockers | Changed in **v0.3.6** (2025-06-08). Existing saves keep beds for compatibility. Patch note: *"Fixed employees saying they need to be assigned to a bed, instead of a locker."* | v0.3.6 notes [verified] |
| Wage collection time | Employees take the daily wage from the container. **They will not take payment if it is placed at 4:00 AM.** | Fandom Employees [community] |
| Working hours | Stop at **4:00 AM** when time pauses. Do not resume until the next day. | Fandom Handlers [community] |
| No pay = no work | Yes, hard requirement | Fandom Employees [community] |
| Firing | Talk → "Your services are no longer required." No refund. | scheduleonewiki.com [community] |
| Property transfer | Added in **v0.3.6** | v0.3.6 notes [verified] |
| Knocked out | Requires **a full day** before work resumes | Fandom Handlers [community] |
| Lockers colour-coded | Slight colour differentiation by employee type (v0.3.6) | v0.3.6 notes [verified] |

### 7.3 Employees per property

| Property | Employee limit | Note |
|---|---|---|
| RV | 0 | Temporary starter |
| Motel Room | 0 | Explicitly cannot host employees |
| Maintenance Office (Sewers) | 0 | |
| Sweatshop | 1 | |
| Storage Unit | 3 | Raised to 3 in v0.3.6 |
| Bungalow | 5 | |
| Barn | 10 | |
| **Docks Warehouse** | **12** | Raised 10 → 12 in v0.4.4 (2026-03-19) |
| **Hyland Manor** | **12** | Raised 10 → 12 in v0.4.0f8 (2025-09-27) |
| **Businesses** (all 4) | **0** | *"cannot have Employees"* |

Sources: <https://schedule-1.fandom.com/wiki/Properties>, <https://schedule-1.fandom.com/wiki/Businesses> [community]; v0.4.0f8 and v0.4.4 Steam notes [verified].

**Total employee ceiling across all properties: 43** (1+3+5+10+12+12). [inference, arithmetic]

### 7.4 The Handler route system — the template for Drivers

This is the single most important shipped system for the Drivers mod. [community — Fandom Handlers, edited 2026-04-08]

| Property | Value |
|---|---|
| Routes per handler | **5** |
| Route shape | Exactly **one source ("from") + one destination ("to")** |
| Valid endpoints | *"almost any type of building with item-slots, including storage racks, machines, and even delivery parking slots"* |
| Per-trip payload | **A single stack of a single item type, per route, per trip** — despite having 5 inventory slots |
| Priority | Routes and machines execute **top-to-bottom in assignment order**; machines outrank routes |
| Tie-break within a route | **Alphabetical by item name** ("they will move Addy before Bananas") |
| Route breakage | Picking up and replacing a machine resets any route using it to "none" |
| Cross-property | **Not possible.** "Fixed employees being able to be assigned to stuff at other properties." (2025 patch) |

**The Driver is precisely: 5 routes, cross-property, vehicle-sized payload.** [inference]

---

## 8. Dealers

Recruited by unlocking a region and reaching **friendly** with one of the dealer's connections, then paying a one-time buy-in. Managed via the **Dealers app** on the phone. [community]

| Dealer | Region | Location | Buy-in | Cut | Max customers (v0.4.3+) | Max customers (≤v0.4.2) |
|---|---|---|---|---|---|---|
| Benji Coleman | Northtown | Motel Room #2 | **$500** | **20%** | **10** | 8 |
| Molly Presley | Westville | Brown Apartment | **$1,000** | **20%** | **10** | 8 |
| Brad Crosby | Downtown | Parking Garage tent | **$2,000** | **20%** | **10** | 8 |
| Jane Lucero | Docks | Docks RV | **$3,000** | **20%** | **10** | 8 |
| Wei Long | Suburbia | Shack behind Green House | **$4,000** | **20%** | **10** | 8 |
| Leo Rivers | Uptown | Shipping container near Church | **$5,000** | **20%** | **10** | 8 |

Sources: <https://schedule-1.fandom.com/wiki/Dealers> [community]; <https://techsngames.com/schedule-1-best-dealer-setup/> (2026) [community]; <https://steamcommunity.com/sharedfiles/filedetails/?id=3649586524> (Shrooms-era guide) [community].

### 8.1 Dealer mechanics

| Mechanic | Value / behaviour | Source |
|---|---|---|
| **Cut** | **Flat 20%, non-negotiable, non-upgradeable.** Identical for all six regardless of buy-in, product value, or rank. | Fandom Dealers, techsngames [community] |
| Total customer slots | **60** across all six (10 × 6) as of v0.4.3, 2026-02-02 | [community] |
| Dealer inventory | **10 hidden inventory slots** | Fandom Dealers [community] |
| Stack handling | Accepts baggies, jars, bricks; **auto-converts jars/bricks down** to whatever quantity the customer wants | Fandom Dealers [community] |
| Selling scope | Dealers sell **all drugs in their inventory**, even products not listed in your Products app | Fandom Dealers [community] |
| **Police immunity** | **Dealers cannot be arrested.** They sell next to checkpoints and active patrols with zero risk. | Fandom Dealers [community] |
| Sale price | Dealers sell at **whatever price you set in the Products app** | Steam guide [community] |
| Payout collection | Cash accumulates on the dealer; **you collect in person** | Steam guide [community] |
| XP per dealer deal | **10 XP** (vs 20 for a direct deal) | Fandom Ranks [community] |
| Buy-in refund | **None** on firing | schedule1wiki.org [community] |
| Cartel dealers | Exist post-v0.4.0; player gets **no XP** for their deals (fixed in v0.4.1f12) | Steam notes [verified] |

**Practical margin math** [community, Steam guide, 2026]: at a 1.4× markup on suggested price, a $100-suggested product listed at $140 nets you $112 after the 20% cut versus $80 at suggested price. The guide's tested figure: 10 jars via Benji at suggested price ≈ $600 returned; the same 10 jars at 1.4× ≈ $850.

---

## 9. Customers

### 9.1 Unlocking and relationships

| Mechanic | Value | Source |
|---|---|---|
| Unlock method | Have one of the customer's **contacts** already in your contacts, then offer a **free sample** | Fandom Customers [community] |
| XP for unlocking | **50 XP** | Fandom Ranks [community] |
| Relationship scale | **0–5** (RelationData; the deal formula uses `RelationDelta / 5f`) | Deal-Optimizer-Mod source [community] |
| Deals to max relationship | **6 successful deals** | Steam Ultimate Guide 2026 [community] |
| Deals to max addiction | **5 successful deals** | Steam Ultimate Guide 2026 [community] |
| Free samples & addiction | Free samples **do not** contribute to addiction, even when accepted | Steam Ultimate Guide 2026 [community] |
| Product visibility cap | Customers only ever consider **the top 4** listed products matching their preferences. Anything past 4 is ignored. | Steam Ultimate Guide 2026 [community] |

### 9.2 Per-customer data profile

Every customer NPC carries a fixed **Customer Data** asset: [community — <https://schedule-1.fandom.com/wiki/Customers>]

- **Budget** — max spend in a single deal. **Per-deal, not weekly or daily.**
- **Order Frequency** — min and max orders per week.
- **Schedule** — a specific `Order Time` and `Preferred Order Day`.
- **Addiction** — `Base Addiction` and `Dependence Multiplier`.
- **Tastes** — `Affinities` per drug type (**−1.00 hates … +1.00 loves**) and `Standards` (minimum quality tier).
- **Call Police Chance** — per-customer, observed range **0% … 67.1%**.

**Order frequency rule** [community]: the game takes the **higher of Addiction and Relationship**, and interpolates between the customer's Min and Max orders/week. Values of 5 or 6 round up to **7**. To order daily (7/week) a customer must be at **100% addiction OR max loyalty**.

**Order window** [community]: an order can only generate in a **2-hour window** starting at the customer's assigned Order Time. (Order Time 16:30 ⇒ contact between 16:30 and 18:30.)

**Addiction dynamics** [community — Steam Ultimate Guide, 2026]:
- A **100%-addictive product raises addiction by 20 percentage points per sale**; lower addictiveness scales linearly.
- Addiction **decays ~7 percentage points per day** after a few days without product. The decay timer resets on *any* sale, even a 0%-addictive one.
- Some customers **start pre-addicted** (Jessi ≈ 40%).
- Around ~50% addiction, customers will **hunt you down at night** to ask for product — but only if you're dealing personally and haven't listed products for sale.

### 9.3 Budget tiers by region (at Peddler III, wiki reference point)

| Region | Standards | Per-deal budget at Peddler III | Example customers |
|---|---|---|---|
| **Uptown** | High | **$1,392 – $1,706** | Fiona Hancock, Herbert Bleuball, Jen Heard, Lily Turner, Michael Boog, Tobias Wentworth, Walter Cussler, Irene Meadows ($1,706); Pearl Moore, Ray Hoffman ($1,392) |
| **Suburbia** | High | **$835 – $1,170** | Alison Knight, Carl Bundy, Chris Sullivan, Dennis Kennedy, Hank Stevenson, Harold Colt, Jack Knight, Jackie Stevenson, Jeremy Wilkinson ($1,170); Karen Kennedy ($835) |
| **Docks** | Moderate / Very Low | **$529 – $1,023** | most at $926; Javier Perez $529 |
| **Downtown** | Moderate / Very Low | **$390 – $682** | most at $682 |
| **Westville** | Low / Very Low | **$222 – $1,048** | George Greene $1,048, Dean Webster $950, Jerry Montero $487, several at $390 |
| **Northtown** | Low / Very Low | **$222 – $1,023** | Geraldine Poon $1,023, Peggy/Peter $682, Jessi $557, Kathy/Austin $445, Beth/Chloe/Ludwig $222 |
| **Serena Flats** (prologue) | Moderate | **$60 – $120** at Street Rat I | Andy $60, Doug $90, Joe $120 |

Source: <https://schedule-1.fandom.com/wiki/Customers> full data table (page edited 2026) [community]. **Budgets scale with player level up to 10× the base, capping around Kingpin 66** — 115 levels above Street Rat I. [community — Fandom Ranks/Customers]

**Order frequency spread observed**: Min 1–7 / Max 5–7 per week. High-frequency customers: Charles Rowland (7/7), Jessi Waters (7/7), Kyle Cooley (7/7), Chloe Bowers (6/7), Kathy Henderson (6/7), Ludwig Meyer (6/7), Mick Lubbin (6/7), Sam Thompson (6/7), Meg Cooley (5/7).

**Rank cash multiplier** (community-derived, applied to a customer's base cash) [community — <https://steamcommunity.com/sharedfiles/filedetails/?id=3467994992>]:

| Rank | Multiplier | Rank | Multiplier |
|---|---|---|---|
| Street Rat I | ×1.00 | Bagman I → V | ×2.00 → ×2.25 |
| Hoodlum V | ×1.50 | Enforcer I → V | ×2.25 → ×2.50 |
| Peddler I → V | ×1.50 → ×1.75 | Underlord V | ×3.25 |
| Hustler I → V | ×1.75 → ×2.00 | Baron I → V | ×3.25 → ×3.50 |
| | | Kingpin I → V | ×3.50 → ×3.90 |

### 9.4 The actual deal-acceptance maths (decompiled)

Recovered from the Deal-Optimizer-Mod, which calls the game's own methods (`Customer.GetValueProposition`, `GetProductEnjoyment`, `CustomerData.GetAdjustedWeeklySpend`, `GetOrderDays`). This is game code, not guesswork. [community — <https://github.com/zocke1r/Deal-Optimizer-Mod/blob/b20a63a7/src/IL2CPP/Core.cs>]

**Spending limits:**
```
adjustedWeeklySpend = customerData.GetAdjustedWeeklySpend(RelationDelta / 5)
orderDays           = customerData.GetOrderDays(CurrentAddiction, RelationDelta / 5)
dailyAverage        = adjustedWeeklySpend / orderDays.Count
maxSpend            = dailyAverage * 3
```
Any offer **at or above `maxSpend` is a guaranteed failure**. The in-mod message is literally *"order must be less than 3× average daily spend."*

**Product appeal (the price ceiling):**
```
productEnjoyment = GetProductEnjoyment(product, Standards.GetCorrespondingQuality())
num2  = price / product.MarketValue
num3  = Lerp(1, -1, num2 / 2)        // == 1 - num2
appeal = productEnjoyment + num3      // == productEnjoyment + 1 - (price / MarketValue)
if (appeal < 0.05) -> no sale, quantity 0
```
**Simplified** [inference, algebra on the above]: **`appeal = enjoyment + 1 − (price / MarketValue)`**, so the maximum price a customer will entertain is

> **`price_max = MarketValue × (enjoyment + 0.95)`**

With `enjoyment ∈ [−1, 1]`, that's a ceiling of **0× market value at worst and ~1.95× market value at best.** This is exactly why the community's "×1.4 suggested price" rule works and why pushing to 1.5–1.6× starts getting rejections.

**Quantity and payment the customer offers:**
```
adjustedSpending = (adjustedWeeklySpend / orderDaysCount) * Lerp(0.66, 1.5, productEnjoyment)
adjustedPrice    = basePrice * Lerp(0.66, 1.5, productEnjoyment)
quantity         = RoundToInt(adjustedSpending / basePrice)
quantity         = Clamp(quantity, 1, 1000)
if (quantity >= 14) quantity = RoundToInt(quantity / 5) * 5   // rounds to nearest 5
payment          = RoundToInt(adjustedPrice * quantity / 5) * 5  // rounds to nearest 5
```

**Counter-offer success probability:**
```
quantityRatio     = Pow(newQty / offeredQty, 0.6)
quantityMultiplier= Lerp(0, 2, quantityRatio * 0.5)
penaltyMultiplier = Lerp(1, 0, Abs(quantityMultiplier - 1))
if (newValueProposition * penaltyMultiplier > valueProposition) -> P = 1.0
if (newValueProposition < 0.12)                                 -> P = 0.0
threshold = Lerp(0, 1, valueDifference / 0.2)
bonus     = Lerp(0, 0.2, Max(CurrentAddiction, NormalizedRelationDelta))
P         = Clamp01((0.9 - (threshold - bonus)) / 0.9)
```
Addiction and relationship each contribute **up to +0.20** to the acceptance threshold — and the game takes the **max**, not the sum.

**Hard numeric ceilings visible in that code:** `quantity` clamps at **1,000**; the internal quantity-rounding kicks in at **14**; both quantity and payment round to multiples of **5**.

### 9.5 Quality tiers

Internally a float 0.0–1.0. [community — <https://schedule-1.fandom.com/wiki/Quality>]

| Tier | Min | Max |
|---|---|---|
| Trash | 0.00 | 0.25 |
| Poor | 0.26 | 0.40 |
| Standard | — | — |
| Premium | — | — |
| Heavenly | — | 1.00 |

**Standards enum** on customers: **Very Low, Low, Moderate, High** (wiki text also mentions *Very High*), mapped by `Standards.GetCorrespondingQuality()` onto the quality tiers. Observed in the shipped customer table: Very Low, Low, Moderate, High only. [community]

Related shipped bonuses: **"exceeded quality" bonus** added v0.4.0f8 (2025-09-27); **"rainy bonus" for deals completed during rain** added v0.4.4 (2026-03-19). [verified]

---

## 10. Police and law

### 10.1 Wanted levels

| Level | Police behaviour | XP for escaping |
|---|---|---|
| **Investigating** | Notified, heading to the location to investigate | — |
| **Under Arrest** | Attempt arrest **without force** (baton drawn, not used) | **20 XP** |
| **Wanted** | Chase and use **stun gun** | **40 XP** |
| **Wanted Dead or Alive** | Chase and use **pistol (M1911)** | **60 XP** |

Source: <https://schedule-1.fandom.com/wiki/Police> (page edited 2026-05-24) [community]; XP values cross-confirmed on <https://schedule-1.fandom.com/wiki/Ranks> [community].

### 10.2 What triggers each level

| Trigger | Resulting level |
|---|---|
| NPC alerted; a phone meter fills (KO the NPC to stop it) | **Investigating** |
| Passing a police checkpoint | **Body Search** |
| Found while Investigating; fleeing a body search; **spotted during curfew**; spotted dealing drugs; spotted pickpocketing | **Under Arrest** |
| Resisting arrest; running over an officer's corpse; spotted attacking or killing an NPC | **Wanted** |
| Attacking or killing an officer; aiming a gun at an officer; resisting arrest long enough; **hitting police with your car** | **Wanted Dead or Alive** |

NPCs call the police if you **discharge a firearm, attack them, or are caught pickpocketing**. Police notice pickpocketing directly as of a 2025 patch. [community + verified patch notes]

### 10.3 Fines and confiscation — the current penalty table

| Crime | Fine |
|---|---|
| Controlled Substance | **$5** |
| Low Severity Drug (Marijuana) | **$10** |
| Medium Severity Drug (Methamphetamine) | **$20** |
| High Severity Drug (Cocaine) | **$30** |
| Failure to Comply | **$50** |
| Evading Arrest | **$50** |
| Vandalism | **$50** |
| Theft | **$50** |
| Brandishing | **$50** |
| Discharge Firearm | **$50** |
| Assault | **$75** |
| **Violating Curfew** | **$100** |
| Attempting to Sell | **$150** |
| Deadly Assault | **$150** |

Source: <https://schedule-1.fandom.com/wiki/Penalties> (page last edited 2025-08-09 — **oldest number set in this document, treat with caution**) [community].

**Confiscation and payment rules** [community]:
- All contraband is confiscated, classified as low / medium / high severity, and **fined per item** — a large haul therefore costs thousands.
- Only crimes committed **between the start of being wanted and the arrest** count. Crimes from an earlier chase you escaped do not.
- **Fines are paid in cash only** — never from your bank balance.
- **No debt is incurred** if you can't pay. Your held cash is simply emptied.
- Patch note (2025): *"Reduced curfew fine."* [verified]

**There is no jail time, no XP loss, and no rank loss on arrest.** [community — absent from every source consulted]

### 10.4 Curfew

| Event | Time | Source |
|---|---|---|
| Curfew warning (30 min) | **8:30 PM** | Fandom Game mechanics [community]; "Added 30 minute curfew warning" [verified] |
| **Curfew starts** | **9:00 PM** | Fandom Game mechanics [community] |
| Police tolerance | **15 minutes** grace before enforcement | v0.3.6-era patch note [verified] |
| Consequence | Spotted during curfew ⇒ **Under Arrest**; **$100** "Violating Curfew" fine | [community] |
| NPCs | Mostly go home, but some stay out **with zero repercussions** — player-only mechanic | <https://schedule-1.fandom.com/wiki/Player> [community] |
| Curfew ends | Effectively at day start, **7:00 AM** | [inference from day schedule] |

### 10.5 Checkpoints, body searches, evasion

**Checkpoints** — pre-defined locations with barriers that block vehicles. To pass legitimately you must exit the vehicle, submit to a body search, then a vehicle search. NPC-driven vehicles also lower the barriers, which players exploit by tailgating. [community]

| Checkpoint (as of v0.4.2f9) | Active from | Active to |
|---|---|---|
| Western | 11 AM | 7 PM |
| Docks | 12 PM | 8 PM |
| North Residential | 8 AM | 12 PM |
| West Residential | 7 AM | 11 PM |

Checkpoints are **lowered when unmanned** (v0.4.0f8). [verified]

**Body searches** trigger when you stare at police for long periods, stand still near them, or approach a checkpoint. During the search you can conceal **one item at a time** by holding left-click. Failing ⇒ **Under Arrest** + pursuit. There is a dedicated body-search fail sound. **No dogs exist in the game.** [community + verified patch notes]

**Escape methods** [community]: outrun them (police move at your speed but path badly); hide in dumpsters / shrubs / behind large trees (fails if an officer has direct LOS when you enter, or if you hide near the NPC who called it in); return to an owned property out of sight; reach geometry police can't path to; kill all pursuers. **Sleeping clears the wanted level entirely** ("Wanted level is now cleared when you sleep") [verified]. The **sewers** (v0.4.1) are an explicit police-free traversal layer. Movement effects (Athletic > Energetic), skateboards and cars all help.

**Police equipment**: flashlight (night patrol), baton (drawn at Under Arrest, never actually used), stun gun (Wanted — electrifies and heavily slows you for a few seconds but doesn't block item use), M1911 pistol (Wanted Dead or Alive — slow fire, poor aim, can shoot through other officers and some objects). Police vehicles: **Shitbox** and **Bruiser** variants. [community]

### 10.6 Existing "heat"-like systems

**There is no heat system.** The only persistent-pressure system in the game is the **Cartel Influence** meter from v0.4.0 (Benzies family), which you reduce by interrupting their graffiti (25 XP per clean). Property "heat" and raids remain an **unshipped Trello card**. [verified + community]

The closest existing scaling is **rank-based**: *"As you level up, the police will start to get more difficult. When you start, only a few police officers will patrol, and even fewer at night. As you level up, you'll begin to see cars and more patrol officers. Eventually, you'll see police SUV's and there will be fences on either side of checkpoints."* [community — Fandom Police]

Plus, as of **v0.4.6 (2026-08-01)**: *"Police patrols, sentries, checkpoints now assign **1-2 officers** instead of 2 all the time."* [verified] — **the only true dynamic-intensity primitive shipped.**

Relevant console commands (all shipped) [community — <https://schedule-1.fandom.com/wiki/Console>]: `raisewanted`, `lowerwanted`, `clearwanted`, `setpoliceignoreplayers`, `settime`, `settimescale`, `setdayduration`, `setquality`, `changecash`, `give`.

---

## 11. Deliveries, vehicles and logistics

### 11.1 Vehicles

| Vehicle | Price | **Cargo slots** | Top speed | 0–40 km/h | Speed rating |
|---|---|---|---|---|---|
| **Shitbox** | **$5,000** | **5** | 53 km/h | 3.1 s | 1.5 |
| **Veeper** (van) | **$9,000** | **16** | 86 km/h | 3.1 s | 2.2 |
| **Bruiser** | **$12,000** | **5** | 68 km/h | 2.7 s | 1.9 |
| **Dinkler** | **$15,000** | **8** | 77 km/h | 3.7 s | 2.1 |
| **Hounddog** | **$25,000** | **5** | 84 km/h | 2.3 s | 2.3 |
| **Hotbox** | **$30,000** | **5** | — | 2.7 s | 2.3 |
| **Cheetah** | **$40,000** | **4** | 93 km/h | 1.8 s | 2.5 |

Source: <https://schedule-1.fandom.com/wiki/Vehicles> (speed data tested at v0.3.4f4 — **stale for speeds, prices/slots still current**) [community]. Bought at **Hyland Auto**, Downtown, card-only, 6 AM–6 PM.

**Vehicles need no fuel, take no collision or weapon damage, and neither occupants nor cargo can be harmed.** [community] This is directly relevant to "heavier consequences when caught."

Skateboards: Cheap Skateboard **$75** at Shred Shack; Golden Skateboard **$1,500**; off-road skateboard added v0.3.6f6. No cargo capacity, and occupies an inventory slot. [community]

### 11.2 Delivery / shipment system

| Mechanic | Value | Source |
|---|---|---|
| **Delivery fee** | **$200 flat per order**, on top of item cost | Destructoid, selphie1999gaming, ScalaCube [community] |
| Requirement | Destination property must have **≥1 loading bay** | [community] |
| Eligible properties | **Storage Unit (1), Bungalow (1), Barn (2), Docks Warehouse (2), Hyland Manor (3)** | Fandom Properties [community] |
| Loading bays purchasable separately? | **No.** Bays come with the property and cannot be added. | [community] |
| Order via | **Deliveries app** on phone → pick store → pick property + dock | [community] |
| Delivery time | "several in-game hours", scaling with quantity | [community] |
| Payment | **Card-only** | Fandom Money [community] |
| Availability | **24/7**, even for Oscar's store (whose warehouse is only open 6 AM–6 PM) | [community] |
| Van behaviour | Parks at the loading bay and **stays until fully unloaded** | [community] |
| **Automation** | A **Handler** assigned to the loading bay will empty the van into storage | Fandom Handlers [community] |
| Progression bug (fixed) | "Fixed deliveries not progressing during sleep" — v0.4.3 | [verified] |

### 11.3 Supplier dead drops and meetups

| Mechanic | Value | Source |
|---|---|---|
| Suppliers | **Albert Hoover** (Northtown, marijuana seeds), **Shirley Watts** (Westville, pseudo), **Fungal Phil** (Downtown, shroom supplies), **Salvador Moreno** (Docks, coca seeds) | Fandom Suppliers [community] |
| Unlock | Reach **Friendly** with one of their connections | [community] |
| Dead-drop order cap | **10 items max per order** | [community] |
| **Dead-drop lead time** | **~30 in-game minutes per item ordered** (4 seeds ⇒ 2 hours) | [community] |
| Debt | Orders accrue debt; higher debt lowers your order limit; pay at the supplier's **Stash**. Overpayment is refundable at the stash. | [community] |
| Meetups (at "loyal") | Supplier waits at one of **4 fixed locations for 6 hours**, no order limit, **cash** purchase. Overflow goes on a pallet that despawns with the supplier. | [community] |
| Delivery unlock (max relationship) | Order seeds/ingredients straight to your location via the **Delivery app**, no dead drop or meetup | [community] |
| Relationship growth | **Only dead-drop orders** raise supplier relationship. Meetups do not. | [community] |

### 11.4 Existing item-transfer automation (the complete list)

1. **Handler routes** — 5 per handler, single source → single destination, **within one property only**, one item stack per trip. Can drain delivery vans.
2. **Machine output destinations** — nearly every production machine has a settable **Destination**; the assigned Botanist/Chemist moves output there without a Handler.
3. **Botanist auto-move** — Botanists move harvested product to drying racks / mixing stations automatically.
4. **Item slot filters** (v0.3.6) — whitelist/blacklist per slot to control what employees put where.
5. **Dealers** — auto-break jars/bricks into customer-sized quantities.

**There is no cross-property transfer of any kind.** Every route is property-local. That gap is precisely the Drivers feature. [inference]

### 11.5 Storage containers

| Container | Price | Slots | Footprint | Unlock |
|---|---|---|---|---|
| Small Storage Rack | **$30** | **4** | 1×2 | Street Rat I |
| Medium Storage Rack | **$45** | **6** | 1×3 | Street Rat I |
| Large Storage Rack | **$60** | **8** | 1×4 | Street Rat I |
| Small Storage Closet | **$150** | **6** | 1×2 | **Bagman I** |
| Medium Storage Closet | **$225** | **8** | 1×3 | Bagman I |
| Large Storage Closet | **$300** | **12** | 1×4 | Bagman I |
| **Huge Storage Closet** | **$500** | **20** | 2×4 | Bagman I |
| **Locker** (employee wage) | — | **6** (max $6,000) | 1×3 | — |

Racks hold **10 per stack** and are accessible from all four sides. Storage Closets shipped in **v0.4.3** (2026-02-02) and are sold at hardware stores. Sources: <https://schedule-1.fandom.com/wiki/Storage_rack>, <https://schedule-1.fandom.com/wiki/Items>, <https://schedule-1.fandom.com/wiki/Locker> [community]; v0.4.3 notes [verified].

---

## 12. Properties and businesses

### 12.1 Properties

| Property | Price | Currency | Location | Size | **Loading bays** | **Employees** |
|---|---|---|---|---|---|---|
| RV | Free | — | Woods SW | 32 tiles | 0 | 0 |
| Maintenance Office ("Throne Room") | Free | — | Sewers | 179 tiles | 0 | 0 |
| **Motel Room** | **$75** | Cash (Donna) | Northtown | 94 tiles | 0 | 0 |
| **Sweatshop** | **$800** | Cash (Mrs. Ming) | Northtown | 148 tiles | 0 | 1 |
| **Storage Unit** | **$4,000** | Online balance | Northtown | 178 tiles | **1** | 3 |
| **Bungalow** | **$6,000** | Online balance | Westville | 398 tiles | **1** | 5 |
| **Barn** | **$25,000** | Online balance | Woods East | 1,211 tiles | **2** | 10 |
| **Docks Warehouse** | **$50,000** | Online balance | Docks | 1,429 tiles | **2** | 12 |
| **Hyland Manor** | **$250,000** | Online balance | Suburbia | 1,753 tiles | **3** | 12 |

Hyland Manor requires the **"Finishing the Job"** quest. All Ray's Realty properties are bought with **Online Balance**, so they are gated by the **$10,000/week ATM deposit cap** plus laundering throughput. All properties start at **20 °C / 68 °F**; the Maintenance Office is **10 °C / 50 °F** (grows shrooms without an AC Unit). Source: <https://schedule-1.fandom.com/wiki/Properties> [community].

### 12.2 Businesses (laundering)

| Business | Price | Location | Size | **Max launderable / 24 h** |
|---|---|---|---|---|
| **Laundromat** | **$4,000** | Downtown | 36 tiles | **$2,000** |
| **Post Office** | **$10,000** | Downtown | 48 tiles | **$4,000** |
| **Car Wash** | **$20,000** | Downtown | 47 tiles | **$6,000** |
| **Taco Ticklers** | **$50,000** | Northtown | 78 tiles | **$8,000** |
| **All four** | **$84,000** | — | — | **$20,000** |

Source: <https://schedule-1.fandom.com/wiki/Businesses> [community].

**Laundering mechanics** [community]:
- Each cycle takes **24 in-game hours = 24 real minutes**, counted from when you start.
- Laundering **continues while time is paused at 4 AM**, so multiple cycles per "day" are theoretically possible.
- Money in laundering is **unavailable as both cash and balance** until it completes.
- Businesses generate **no income of their own**.
- Businesses have small buildable areas (shelving included) but **cannot host employees** and have **no water source** — meth labs are the only practical production there (Taco Ticklers fits 2).

---

## 13. Money and time scale

### 13.1 The day

| Time | Event |
|---|---|
| **7:00 AM** | **Start of day** |
| 4:00 PM | Casino opens |
| 6:00 PM | Shops close; **Warehouse opens** (Manny + Oscar) |
| 8:30 PM | Curfew warning |
| **9:00 PM** | **Curfew starts** (+15 min police tolerance) |
| **4:00 AM** | **Time pauses.** Employees stop. Plants stop growing. Machines keep running. Sleep to advance. |
| 6:00 AM | Warehouse closes (only observable via console) |

Source: <https://schedule-1.fandom.com/wiki/Game_mechanics> (edited 2026-07-10) [community].

### 13.2 Time conversion

| Conversion | Value | Evidence |
|---|---|---|
| **1 in-game hour** | **≈ 1 real minute** | Laundering: "24 in-game hours (24 real-life minutes)"; Drying rack: "1 Quality Level per 12 in-game hours (12 minutes)"; Meth: chemistry station 8 h = 8 min irl, lab oven 6 h = 6 min irl [community] |
| **Full 24 h cycle** | **24 real minutes** | Fandom Game mechanics [community] |
| **Playable window (7 AM → 4 AM)** | **21 in-game hours ≈ 21 real minutes** | [inference, arithmetic] |
| Console override | `setdayduration`, added v0.4.3; `settimescale <multiplier>` (default **1**) | [verified / community] |
| Day indexing | 1 = Monday … 7 = Sunday; customer `Preferred Day` uses 0 = Monday | ScheduleLua docs, Fandom Customers [community] |

### 13.3 Money flow

| Mechanic | Value |
|---|---|
| **ATM deposit cap** | **$10,000 per week, per player.** Withdrawals don't count against it. |
| Laundering cap | **$20,000 / 24 h** with all four businesses |
| Cash-only vendors | Suppliers (dead drops & meets), Oscar's Store, Casino, Pawn Shop, Cuke machines, Stan Carney (weapons), **Manny (hiring employees)**, **paying employees** |
| Card-only vendors | Hardware stores, Gas-Mart, Hyland Auto, Ray's Realty, Bleuball's, Thrifty Threads, Barbershop, **all deliveries** |
| Exceptions | Police fines and hospital bills accept either |
| Cash stack size | 1,000 |
| Net worth | Inventory + storage. **Product/cash held by your dealers does NOT count.** |

Source: <https://schedule-1.fandom.com/wiki/Money> [community].

**Critical structural fact for wage design** [inference]: employees are paid in **cash**, but every meaningful expansion (properties, businesses, vehicles, equipment) is bought with **online balance**, which is throttled to **$10,000/week deposit + $20,000/24h laundering**. Wages therefore compete with nothing — cash is abundant, balance is scarce. **Adding a high daily wage is a weak brake; adding a high signing fee that must be paid in cash is also weak. The real cost lever in this game is the property employee slot.**

### 13.4 Income curve (for calibrating "does this number feel early or late?")

| Phase | Anchors |
|---|---|
| **Early (Street Rat → Hoodlum)** | Customer per-deal budgets **$60–$450**. Motel $75, Sweatshop $800. Cleaner $100/day is a real cost. Storage Unit $4,000 is a milestone purchase. |
| **Mid (Peddler → Bagman)** | Per-deal budgets **$390–$1,170**. Bungalow $6,000, Barn $25,000. Dealer buy-ins $500–$5,000. Chemist at $300/day is trivial. Bound by the **$10,000/week deposit cap**. |
| **Late (Enforcer → Kingpin)** | Uptown per-deal budgets **$1,392–$1,706 at Peddler III**, scaling to **10× base** near Kingpin 66 (i.e. **$13,900–$17,060+ per deal**). Manor $250,000. Bound by **$20,000/24h laundering**, i.e. roughly **$140,000/week** ceiling on convertible income. |

XP economy [community — Fandom Ranks]: quest 25–100 · new mix 80 · **escape Wanted Dead or Alive 60** · unlock customer 50 · graffiti 50 · escape Wanted 40 · clean cartel graffiti 25 · escape Under Arrest 20 · **direct deal 20** · **dealer deal 10** · successful counter-offer 5 · harvest 5 · pickpocket 2. Rank thresholds: Street Rat I at 200 XP; **Kingpin I at ~59,100 total XP**.

---

## 14. Balance numbers we will use

Every number is anchored to a shipped value. Where I invent, I say so.

### 14.1 Hireable Drivers

| Parameter | Value | Justification (anchored to a shipped number) |
|---|---|---|
| **Signing fee** | **$1,500 base, +$100 per existing employee** | Sits between Handler ($1,000) and Chemist ($2,000); the driver is a superset of the Handler. The **+$100/employee escalation is the shipped Handler rule** — reusing it makes the hire dialogue feel native. |
| **Daily wage** | **$250/day** | Directly between Handler ($200) and Chemist ($300). Logistics role, but cross-property, so it costs more than a Handler. |
| **Employee slot cost** | **1 slot on its home property** | Every shipped employee consumes a slot; the 43-slot global ceiling is the real balance lever (§13.3). |
| **Routes per driver** | **5** | **Exact parity with the shipped Handler's 5 logistics routes.** Tyler's own wording is "designate automatic transit routes." |
| **Route shape** | 1 source + 1 destination, top-to-bottom priority, alphabetical item tie-break | Verbatim reuse of shipped Handler route semantics — keeps the clipboard UI grammar identical. |
| **Cargo capacity** | **= the assigned vehicle's cargo slots (4–16)** | No invented number. Shitbox 5 · Dinkler 8 · **Veeper 16**. The Veeper at $9,000/16 slots is the natural "driver van" and is already priced against its capacity. |
| **Departure trigger** | **Minimum item count in vehicle, player-set, default 50% of vehicle capacity** | Tyler's exact example: *"specify the required conditions (e.g. number of items in the vehicle) for a route to begin."* |
| **Load/unload time** | **30 in-game minutes per stop** | The shipped supplier system uses **"around 30 minutes in-game time per item ordered"** — 30 min is the game's existing logistics quantum. |
| **Travel time** | **1 in-game hour (= 1 real minute) per inter-district leg**, ±30 min by vehicle speed rating | 1 in-game hour = 1 real minute is the shipped time constant. Delivery vans already take "several in-game hours". |
| **Round-trip throughput** | **~2 h in-game per 2-stop route** ⇒ **~10 completed routes per 21-hour day** | Derived: 2× travel (1 h) + 2× load/unload (0.5 h). Keeps a Veeper driver at ~160 item-slots/day — meaningful but not free. |
| **Working hours** | **7:00 AM – 4:00 AM**, wage drawn from locker, refuses to work unpaid, won't take pay placed at 4:00 AM | Verbatim shipped employee contract. |
| **Valid endpoints** | Any item-slot container on **any owned property**, **business** shelving, **dealer** inventory, and **loading-bay delivery vans** | Ballot text names properties/businesses/dealers; delivery-van draining is the shipped Handler capability. |
| **Cash-collection leg** | Dealer cash → business, capped at that business's launder cap (**$2,000 / $4,000 / $6,000 / $8,000**) | The launder caps are shipped. PC Gamer's card quote: "collect cash and bring them to your businesses to be laundered." |
| **Dealer top-up cap** | **10 items** per dealer visit | Dealers have exactly **10 hidden inventory slots** (shipped). |
| **Arrest risk** | **None** (driver is police-immune) | Shipped precedent: **"Dealers cannot be caught by police."** A driver that gets arrested would be a support nightmare and breaks parity. |

### 14.2 Police Improvements

**Design spine:** a persistent **Heat** scalar (0–100) that survives sleep, distinct from the momentary wanted level (which is cleared by sleeping). Heat drives officer density, then federal agents, then Outlaw status.

| Parameter | Value | Justification |
|---|---|---|
| **Heat scale** | **0–100**, persists across days | Must survive sleep or it duplicates the shipped wanted level. |
| **Heat gain per crime** | **fine ÷ 5** | Uses the **shipped penalty table as the game's own severity ranking** — no new opinion needed. Yields: Controlled Substance +1 · Low drug +2 · Medium +4 · High +6 · Evading/Vandalism/Theft/Brandishing/Discharge +10 · Assault +15 · **Curfew +20** · **Attempting to Sell +30** · Deadly Assault +30. |
| **Heat gain per arrest** | **+15** | ≈ the Assault fine tier; an arrest should cost more than a single misdemeanour. [inference] |
| **Heat decay** | **−10 per in-game day slept** | Sits between the shipped addiction decay (**−7%/day**) and the shipped total wipe of the wanted level on sleep. 10 days to fully cool from 100. |
| **Tier 0 — Calm (0–19)** | **1 officer** per patrol/sentry/checkpoint | **Exactly the lower bound v0.4.6 shipped**: "assign 1-2 officers instead of 2 all the time." |
| **Tier 1 — Alert (20–39)** | **2 officers** per post; body-search chance ×1.25 | The shipped upper bound. |
| **Tier 2 — Crackdown (40–59)** | **2–3 officers**; all 4 checkpoints active **outside** their normal windows; patrol cars on the road | Extends the shipped rank curve ("you'll begin to see cars and more patrol officers"). Checkpoint windows are shipped data (§10.5). |
| **Tier 3 — Task Force (60–79)** | **3–4 officers**; police SUVs; checkpoint fences up; body-search chance ×2 | SUVs + checkpoint fences are the shipped top of the rank curve — Heat pulls them forward. |
| **Tier 4 — Federal (80–100)** | **Federal agents spawn** | See below. |
| **Federal agent trigger** | Heat ≥ **80** held for **1 full in-game day**, **OR** cumulative confiscated value ≥ **$10,000**, **OR** ≥ **3 arrests in 7 in-game days** | **$10,000 is the shipped weekly ATM deposit cap** — the game's own definition of "more money than a normal person moves". 7 days matches the shipped weekly order cycle. |
| **Federal agent loadout** | Spawn **2 per event**; behave at **Wanted Dead or Alive** (M1911) from first contact; improved aim; do not drop pursuit on brief LOS loss | Reuses the shipped WDoA behaviour and the shipped "police share line-of-sight" system, rather than inventing new AI. |
| **Federal agent XP** | **60 XP** for escaping | Exact parity with the shipped **"Escaping Wanted Dead or Alive — 60 XP"**, the game's largest non-quest award. |
| **Outlaw threshold** | Heat ≥ **90**, or surviving a federal encounter | Top of the heat band; a state, not a slider — matching the ballot's "outlaw **status**". |
| **Outlaw: fine multiplier** | **×2** on every entry in the penalty table | Doubles a $150 "Attempting to Sell" to $300. Deliberately conservative: the current penalty table is 2025-era data and may already have drifted. |
| **Outlaw: confiscation extends to vehicle cargo** | Yes | Directly closes the shipped loophole that vehicle cargo is "effectively invulnerable" — the clearest reading of "heavier consequences when caught". |
| **Outlaw: dealer cut** | **20% → 25%** while Outlaw | The shipped cut is a flat, never-changing 20%. A **+5 percentage point** hazard premium is felt immediately without breaking the mental model. |
| **Outlaw: customer snitch chance** | **+10 percentage points** to each customer's Call Police Chance | Shipped chances already span **0% – 67.1%**, so +10pp stays inside the existing distribution. |
| **Outlaw: legitimate services** | Ray's Realty and Hyland Auto refuse service (card-only vendors) | Uses the shipped **cash-only vs card-only** vendor split as the mechanic. Cash trade still works — you're an outlaw, not bankrupt. |
| **Outlaw clear condition** | **3 consecutive in-game days with zero crimes**, or pay a **$25,000** "legal fee" (cash) | $25,000 = **the shipped Barn price**, a genuine mid-game milestone. Three clean days ≈ the 30 heat you'd shed at −10/day. |
| **Jail time on arrest (Outlaw only)** | Force-advance to the next day + forfeit **1 day of every employee's wage** | The game has no jail. This reuses the shipped sleep transition and the shipped daily-wage draw instead of building a jail scene. |

### 14.3 Special Customers

| Parameter | Value | Justification |
|---|---|---|
| **Groups** | **Bikers, Businessmen, Hippies, Party bus, Rock band** | Verbatim from Tyler's 2026-04-26 announcement. Do not invent extra groups for v1. |
| **Preferred drug types** | **1–2 per group.** Bikers → **Meth** *(sourced)*. Businessmen → **Cocaine** *(sourced)*. Hippies → Weed + Shrooms *[inference]*. Rock band → Cocaine + Shrooms *[inference]*. Party bus → Shrooms + Meth *[inference]*. | "one or two preferred drug types" is verbatim. Biker→meth and businessmen→cocaine are quoted from the 2025 Trello card via PC Gamer. The other three are mine and should be config-exposed. |
| **Group size** | **4–8 members** | A dealer books **10 customers** (shipped v0.4.3 cap). A group should read as a mini-region without exceeding one dealer's book. |
| **Visit cadence** | **One group arrives every 2–3 in-game days**; only one group in town at a time | Shipped customers order **1–7 times per week** (median ~3 ⇒ every ~2.3 days). Matching that beat delivers the stated goal of "more 'rhythm'". |
| **Stay duration** | **One full day window: 7:00 AM → 4:00 AM** | The shipped playable day. Leaving at the time-pause needs no new despawn logic. |
| **Arrival notice** | Phone message at **7:00 AM** on arrival day + a map marker | Shipped customers already contact you via the Messages app; the map already marks customers, dealers, dead drops, meetups. |
| **Order window** | Single group order, generated in a **2-hour window** from a per-group Order Time | **Exactly the shipped customer rule**: "an order can only be generated during a 2-hour window starting at that specific time." |
| **Group budget** | **5× the highest shipped per-deal budget of the current rank tier.** At Peddler III that is **5 × $1,706 = $8,530**; scales on the same curve to **~$85,300** near Kingpin 66 | Rides the shipped budget curve exactly (base budget × up to 10× by rank), so it can never desync from the game's own economy after a patch. |
| **Order quantity** | **40–80 units, rounded to the nearest 5** | Derived from the shipped quantity formula. A top Uptown customer at Peddler III ($1,706, ~$150/unit product) buys ~11 units; ×5 ⇒ ~55. Clamp 40–80. **Rounding to 5 is the shipped rule** (`if quantity >= 14, round to nearest 5`). |
| **Price multiplier** | **0.85 × the price a normal customer would accept**, i.e. ceiling = **`MarketValue × (enjoyment + 0.95) × 0.85`** | This is the literal implementation of **"albeit at a slightly lower profit margin"**, computed against the decompiled shipped appeal formula (§9.4). At typical play (1.4× market) it lands the group's ceiling near **1.19× market** — a clean ~15% margin haircut. |
| **Standards** | Bikers **Very Low** · Party bus **Low** · Hippies **Low** · Rock band **Moderate** · Businessmen **High** | Uses only the four Standards values that actually appear in shipped customer data. Low standards on the bulk-meth buyer is what makes them an inventory sink. |
| **Hard quantity clamp** | **1,000** | The shipped `Mathf.Clamp(quantity, 1, 1000)`. |
| **Max spend guard** | Reject offers **≥ 3× the group's average daily spend** | The shipped `maxSpend = dailyAverage * 3` rule, applied unchanged. |
| **Call police chance** | **0%** | Matches shipped **Cranky Frank (0%)** and the shipped dealer police-immunity. A bulk buyer who snitches would be pure frustration. |
| **XP per group order** | **60 XP** | Parity with the largest shipped non-quest award (escaping Wanted Dead or Alive), and 3× the shipped direct-deal award of 20. |
| **Addiction / relationship** | **None.** Groups are transient — no relationship bar, no addiction meter | They travel; a persistent bond contradicts "periodically visit town". Keeps them out of the shipped relationship/addiction save data. |
| **Interaction with dealers** | Groups **cannot** be assigned to dealers | Preserves the stated design intent: a *fast manual* way to dump bulk. Assigning them to a dealer would just be −20% on top of the −15% margin. |

---

## Sources (B)

**Primary — official (highest trust)**

- <https://trello.com/b/VQQpru3F/schedule-i-roadmap> — Official Schedule I Trello roadmap, sole member Tyler (tyler_tvgs). Board last activity **2026-07-13**. Fetched as JSON on 2026-08-03. **Highest trust for card names/lists; card descriptions are stripped/encrypted in the current public export.**
- <https://store.steampowered.com/news/app/3164500/view/1830163047259398> — "Community vote #3 is now open!", **2026-04-17**. **The verbatim source of all three feature descriptions.** Highest trust, current.
- <https://store.steampowered.com/news/app/3164500/view/1830797770239647> — "'Special customers' wins community vote #3", **2026-04-26**. Vote counts, the five group names, the "slightly lower profit margin" design statement. Highest trust, current.
- <https://store.steampowered.com/news/app/3164500/view/1809235871707524> — "Community vote #2 is now open!", **2025-09-01**. **The detailed Hireable Drivers spec** (transit routes + numeric trigger conditions). Highest trust; wording is 11 months old but the feature is still unshipped, so it stands.
- <https://store.steampowered.com/news/app/3164500/view/1809869180154298> — "'Shrooms' wins community vote #2", **2025-09-08**. Drivers' 2nd place at 139,000 votes.
- <https://store.steampowered.com/news/app/3164500/view/1801617199549230> — "Community vote #1 is now open!", **2025-06-10**. Lists "Police Expansion Update". No description given.
- <https://store.steampowered.com/news/app/3164500/view/1802354289676065> — "'Rival Cartel' wins community vote #1", **2025-06-16**. 440,000+ submissions, 64% cartel; the "non-winning choices will be available again" promise.
- <https://store.steampowered.com/news/app/3164500/view/1839676055889605> — **v0.4.6 patch notes, 2026-08-01. The current version.** Contains the Special Customers status update and the "1-2 officers instead of 2" police change. Highest trust, 2 days old.
- <https://store.steampowered.com/news/app/3164500/view/1836506165582327> — v0.4.6 Open Beta, 2026-07-09. Full change list.
- <https://store.steampowered.com/news/app/3164500/view/1830163047259391> — Patch v0.4.5f2, 2026-04-17. "Added choice descriptions to the community vote panel."
- <https://store.steampowered.com/news/app/3164500/view/1828441623108889> — v0.4.5 Anniversary Update, 2026-03-30.
- <https://store.steampowered.com/news/app/3164500/view/1827626365752067> — v0.4.4 Weather Update, 2026-03-19. Police sentry additions, warehouse worker limit 10→12, "rainy bonus".
- <https://store.steampowered.com/news/app/3164500/view/1823191198612472> — v0.4.3 Storage Closets, 2026-02-02. Closet slot counts, dealer app redesign, `setdayduration`.
- <https://store.steampowered.com/news/app/3164500/view/1819386365108685> — v0.4.2 Shrooms Update, 2025-12-26. Cleaners 3→6 trash cans.
- <https://store.steampowered.com/news/app/3164500/view/1815034432980586> — v0.4.1 Halloween Update, 2025-11-02. Sewers as a police-free layer.
- <https://store.steampowered.com/news/app/3164500/view/1811772772305948> — Patch v0.4.0f8, 2025-09-27. Manor 10→12, checkpoint/vision/pursuit tweaks, "exceeded quality" bonus.
- <https://store.steampowered.com/news/app/3164500/view/1809235871567996> — v0.4.0 Rival Cartel, 2025-08-27.
- <https://store.steampowered.com/news/app/3164500/view/1801617199481467> — v0.3.6, 2025-06-08. **Beds → lockers**, item slot filters, employee property transfers, Storage Unit capacity 3.
- <https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=3164500&count=500&maxlength=0> — Steam news API. **80 items, complete archive back to 2024-09-04.** Captured to `research-ext/raw/steam-news-*`. Highest trust, current.

**Community — wikis (good trust; check the per-page edit date, given below)**

- <https://schedule-1.fandom.com/wiki/Employees> — employee costs, wages, assignable stations, per-property limits. Current (post-locker). **Note: hire-cost ranges reflect the +$100/employee escalation.**
- <https://schedule-1.fandom.com/wiki/Handlers> — edited **2026-04-08**. The single best source on the shipped route system. High trust, recent.
- <https://schedule-1.fandom.com/wiki/Dealers> — buy-ins, 20% cut, 10-customer cap with explicit v0.4.3/v0.4.2 footnotes. High trust, recent.
- <https://schedule-1.fandom.com/wiki/Customers> — full per-customer data table (budget, affinities, standards, orders/week, order time, call-police chance) plus the demand-simulation writeup. Excellent, page edited 2026.
- <https://schedule-1.fandom.com/wiki/Police> — edited **2026-05-24**. Wanted levels, triggers, checkpoints (v0.4.2f9), body searches, evasion, equipment. High trust, recent.
- <https://schedule-1.fandom.com/wiki/Penalties> — last edited **2025-08-09**. **Oldest data in this document — the fine table may have drifted; re-verify in game before shipping.**
- <https://schedule-1.fandom.com/wiki/Game_mechanics> — edited **2026-07-10**. Day schedule and time. High trust, very recent.
- <https://schedule-1.fandom.com/wiki/Money> — deposit cap, laundering, cash-vs-card vendor split, net worth.
- <https://schedule-1.fandom.com/wiki/Businesses> — business prices and launder caps.
- <https://schedule-1.fandom.com/wiki/Properties> — prices, sizes, loading bays, employee limits.
- <https://schedule-1.fandom.com/wiki/Vehicles> — prices and cargo slots current; **speed data tested at v0.3.4f4 (2025) and is stale.**
- <https://schedule-1.fandom.com/wiki/Ranks> — XP thresholds, unlocks, full XP-earning table.
- <https://schedule-1.fandom.com/wiki/Suppliers> — dead drops, 10-item cap, 30 min/item, debt, meetups, delivery unlock.
- <https://schedule-1.fandom.com/wiki/Storage_rack>, <https://schedule-1.fandom.com/wiki/Locker>, <https://schedule-1.fandom.com/wiki/Items> — container slot counts and prices.
- <https://schedule-1.fandom.com/wiki/Quality> — internal 0.0–1.0 quality bands.
- <https://schedule-1.fandom.com/wiki/Updates> — version/date index; **already lists "v0.5.0: Special Customers Update — 2026.08.??"**. Recent.
- <https://scheduleonewiki.com/wiki/Employees>, <https://scheduleonewiki.com/wiki/Dealers> — smaller independent wiki; corroborates the 20% cut and wage model but its dealer page still says 8 customers (**pre-v0.4.3, stale**).

**Community — decompiled game logic (very high trust for formulas, moderate for currency)**

- <https://github.com/zocke1r/Deal-Optimizer-Mod/blob/b20a63a7/src/IL2CPP/Core.cs> — **calls the game's own methods.** Source of the deal-acceptance, appeal, quantity, payment and spending-limit formulas in §9.4. Highest trust for *structure*; constants could have shifted in a later patch.
- <https://deepwiki.com/zocke1r/Deal-Optimizer-Mod> — architecture summary of the same mod.
- <https://github.com/xyrilyn/Deal-Optimizer-Mod/blob/main/README.md> — fork README; confirms max-daily-spend and counteroffer behaviour.
- <https://schedulelua.github.io/ScheduleLua-Docs/api/world/game-time.html> — modding API docs; day indexing 1=Monday, night 20:00–06:00.

**Community — guides and press (moderate trust; verify numbers against the wiki)**

- <https://www.pcgamer.com/games/sim/schedule-1-roadmap-future-plans-for-the-drug-dealing-sim-include-a-classic-fishing-minigame-plus-parkour-and-heroin/> — **2025-04-01. The most valuable secondary source in this document**: direct verbatim quotes from the original Trello card descriptions for travelling customers, drivers and police, back when they were public. Now irreplaceable.
- <https://www.pcgamesn.com/schedule-1/roadmap> — full 2025 roadmap card-title list, incl. "Hireable drivers and logistics workers" and "More police interactions (including bribery)". Titles trustworthy, no dates.
- <https://mein-mmo.de/en/schedule-1-and-its-roadmap-all-content-you-can-expect-in-the-future,1245998/> — independent 2025 read of the same board; corroborates bribery and the police evidence locker.
- <https://gamescout.co.uk/2026/04/schedule-1-special-customers-update/> — **2026-04**. Accurate restatement of the vote #3 result and group list. Recent.
- <https://beefsuplex.com/news/schedule-one-community-vote-three/> — **2026-04-26**. Gives the vote-share percentage (36.9%) and the voting window (Apr 17–24, 2026). Recent, small outlet.
- <https://mein-mmo.de/en/developer-shows-his-work-on-schedule-1-on-twitch-reveals-release-window,1253317/> — **2025-04-24 dev Twitch stream, summarised from Reddit** (VOD is gone). Confirms the vote #1 option set and long-term plans (MDMA after shrooms, purchasable casino for laundering, more laundering businesses). Second-hand; treat as directional.
- <https://steamcommunity.com/sharedfiles/filedetails/?id=3454740900> — "The Ultimate Guide For Schedule 1 (UPDATED 2026)". Source of the addiction rates (+20%/sale, −7%/day), the 5-vs-6 deals to max, the top-4-products cap, and container slot counts. Detailed and recent; player-tested rather than datamined.
- <https://steamcommunity.com/sharedfiles/filedetails/?id=3467994992> — "Pricing Guide For Schedule I". Full rank-multiplier ladder (×1.00 → ×3.90) and the ×1.4 markup heuristic. Community-derived.
- <https://steamcommunity.com/sharedfiles/filedetails/?id=3649586524> — Shrooms-era dealer guide. Confirms 20% cut, v0.4.3 10-customer cap, dealer police immunity, jar/brick auto-splitting.
- <https://techsngames.com/schedule-1-best-dealer-setup/> — 2026 dealer guide. Confirms the v0.4.3 cap change and Uptown weekly-spend range. Moderate trust, SEO-flavoured.
- <https://www.dexerto.com/gaming/how-to-hire-assign-workers-in-schedule-1-3171655/> — **2025, stale.** Lists Cleaner sign-on at $1,500 and Handlers at 3 stations. Included only to document the disagreement.
- <https://selphie1999gaming.com/game-guides/schedule-i/schedule-1-delivery-guide-how-to-order-items/>, <https://www.destructoid.com/how-to-get-a-loading-dock-in-schedule-1/>, <https://scalacube.com/blog/schedule-1/how-to-get-loading-docks-in-schedule-1> — three independent confirmations of the **$200 flat delivery fee** and the loading-bay requirement.
- <https://gameriv.com/schedule-1-all-businesses-operating-hours/>, <https://primagames.com/tips/operating-hours-of-all-businesses-in-schedule-1> — store opening hours (Warehouse 6 PM–6 AM etc.).
- <https://consolepcgaming.com/schedule-1s-latest-beta-is-built-for-controller-play/> — v0.4.6 beta coverage; independently quotes the "1-2 officers" police line. 2026, recent.
- <https://www.destructoid.com/schedule-1-trello-and-discord-links/> — confirms the Trello board and Discord are the two official community channels.

**Not obtainable**

- The **official Discord** (<https://discord.gg/qKMRFzgSmg>) is not scrapable and no verbatim dev quotes about drivers, federal agents or outlaw status were found reposted on Reddit, Steam discussions or in press coverage. **No dev commentary on these three features exists outside the Steam announcements quoted above.**
- The **original Trello card descriptions** are gone from the public export and unrecoverable from Wayback (client-rendered SPA). The April 2025 PC Gamer / PCGamesN / mein-mmo quotes are the only surviving record.
- The **2025-04-24 dev Twitch VOD** is deleted; only the Reddit summary survives.
- **No numeric spec** (wages, capacities, tiers, thresholds) was ever published for any of the three features. **Every number in §14 is derived by me from shipped values — none is quoted from the developer.**
