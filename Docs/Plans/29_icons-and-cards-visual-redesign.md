# Plan 29 — Icons and Cards Visual Redesign

## Goal

Bring the in-game UI into alignment with the `Design/02_icons_and_cards` scope: navy/gold palette throughout, properly-tinted stat icons, structured action cards (header bar + art + body + cost), compact portrait-first country character cards, distinctive dark "cultish" org character cards, icon buttons on the org-bar toggles, and visual success/fail feedback on the card-test overlay.

## Approach

Work layer by layer — shared styles first (CSS classes needed everywhere), then the USS files for each feature, then UXML structural changes, then the C# view builders that construct VisualElement trees. No new assemblies or assets are needed; all required PNG icons already exist in `Assets/UI/Icons/`.

Icon tinting uses `-unity-background-image-tint-color` because Unity USS does not support the `filter:` property. The colour values below are RGB approximations of the HTML design's CSS `filter` values.

USS scope reminder: classes assigned to VisualElements built in C# resolve against the stylesheets loaded by the **document** that contains those elements, not the file where the C# lives. `OrgActionsView` and both character-view classes add elements to the Overlay document (`OrgInfo.uxml`), so new classes that those builders use must be declared in `OrgInfo.uss`, `OrgActions.uss`, or `SharedStyles.uss`.

---

## Step 1 — SharedStyles.uss: new icon tint classes

**File:** `Assets/UI/Shared/SharedStyles.uss`

Add the following blocks after the existing `.gs-icon--coin` rule. These cover all 12 stat icons referenced in Section 1 of the design.

```css
/* --- Stat icon size base (used for in-card / HUD icons) --- */
.gs-icon--stat {
    width: 20px;
    height: 20px;
    -unity-background-scale-mode: scale-to-fit;
}

/* tint-gold: coin, crown, skill, actions */
.gs-icon--tint-gold {
    -unity-background-image-tint-color: rgb(200, 160, 64);
}

/* tint-inf: control (cyan-blue) */
.gs-icon--tint-inf {
    -unity-background-image-tint-color: rgb(64, 160, 220);
}

/* tint-mil: swords (purple/magenta) */
.gs-icon--tint-mil {
    -unity-background-image-tint-color: rgb(180, 64, 220);
}

/* tint-dip: diplomacy (green) */
.gs-icon--tint-dip {
    -unity-background-image-tint-color: rgb(64, 180, 80);
}

/* tint-eco: trade (orange) */
.gs-icon--tint-eco {
    -unity-background-image-tint-color: rgb(220, 140, 40);
}

/* tint-sec: dagger (teal) */
.gs-icon--tint-sec {
    -unity-background-image-tint-color: rgb(40, 160, 150);
}

/* tint-light: compass, characters, agents (neutral warm white) */
.gs-icon--tint-light {
    -unity-background-image-tint-color: rgb(240, 232, 208);
}
```

Background-image rules for the 12 icons (get GUIDs from the respective `.png.meta` files — examples for the icons whose GUIDs are already known):

```css
/* Convenience single-class combinations used in USS references */
.gs-icon--png-coin {
    background-image: url("project://database/Assets/UI/Icons/coin.png?fileID=2800000&guid=46e003f67ebde7347a8004dd120d478a&type=3#coin");
}
.gs-icon--png-control {
    background-image: url("project://database/Assets/UI/Icons/control.png?fileID=2800000&guid=5000d0a84170c354f8e76b2c31b7e13e&type=3#control");
}
.gs-icon--png-crown {
    background-image: url("project://database/Assets/UI/Icons/crown.png?fileID=2800000&guid=5c327d21e97f1c84fbb44bdc989b7910&type=3#crown");
}
.gs-icon--png-swords {
    background-image: url("project://database/Assets/UI/Icons/swords.png?fileID=2800000&guid=f18307f7d5add024cb4775ed58a51415&type=3#swords");
}
/* Add .gs-icon--png-diplomacy, trade, dagger, compass, skill, characters, agents, actions
   using GUIDs from their respective .meta files once retrieved during implementation. */
```

Note: For the org-bar buttons (Step 4), the icon background-image URLs are set inline in USS or in C# on the VisualElement rather than via these shared classes, because the button icon size differs (20px) from the card stat icons.

---

## Step 2 — SharedStyles.uss: compact country character card classes (`.char-card`)

**File:** `Assets/UI/Shared/SharedStyles.uss`

Append a new section for the compact portrait-first country character card style. The existing `.character-card` block is left in place because other code may reference it; the views will switch to the new class.

```css
/* ===== Compact country character card (.char-card) ===== */

.char-card {
    flex-direction: column;
    width: 160px;
    margin: 4px;
    border-width: 1px;
    border-color: rgb(200, 160, 64);
    border-radius: 4px;
    overflow: hidden;
    background-color: rgb(14, 26, 46);
}

.char-portrait-area {
    width: 160px;
    height: 160px;
    overflow: hidden;
    background-color: rgb(106, 136, 152);
    align-items: center;
    justify-content: center;
    -unity-background-scale-mode: scale-and-crop;
}

.char-info {
    padding: 4px 5px;
    background-color: rgb(26, 42, 72);
    flex-direction: column;
}

.char-name {
    font-size: 16px;
    -unity-font-style: bold;
    color: rgb(240, 232, 208);
    white-space: normal;
    -unity-font: url("project://database/Assets/UI/Fonts/Cinzel-Bold.ttf?fileID=12800000&guid=f580297c1d9f7c74984732caed69c108&type=3#Cinzel-Bold");
}

.char-role {
    font-size: 14px;
    -unity-font-style: italic;
    color: rgb(136, 136, 168);
    -unity-font: url("project://database/Assets/UI/Fonts/IMFellEnglish-Italic.ttf?fileID=12800000&guid=ecf34c4fafdd2a546abecbc8943f73bb&type=3#IMFellEnglish-Italic");
}

.char-stats {
    flex-direction: row;
    flex-wrap: wrap;
    margin-top: 2px;
}

.char-stat-chip {
    flex-direction: row;
    align-items: center;
    font-size: 14px;
    color: rgb(200, 184, 144);
    min-width: 60px;
    /* Intentionally no visible background — Unity USS does not support fractional alpha.
       Use rgb(22, 30, 48) here if a subtle dark chip tint is desired. */
    background-color: transparent;
    border-radius: 2px;
    padding: 1px 3px;
    margin: 1px;
}

.char-stat-icon {
    width: 14px;
    height: 14px;
    -unity-background-scale-mode: scale-to-fit;
    margin-right: 2px;
}
```

Note: `rgba()` with fractional alpha (e.g. `rgba(255,255,255,0.07)`) is not supported in Unity USS. The chip background above is set to `transparent`; change to `rgb(22, 30, 48)` if a subtle dark tint is desired during implementation.

---

## Step 3 — SharedStyles.uss: org character card classes (`.org-char-card`)

**File:** `Assets/UI/Shared/SharedStyles.uss`

Append a new section for the cultish org card style.

```css
/* ===== Org character card (.org-char-card) ===== */

.org-char-card {
    flex-direction: column;
    width: 160px;
    margin: 4px;
    border-width: 2px;
    border-color: rgb(160, 120, 24);
    border-radius: 4px;
    overflow: hidden;
    background-color: rgb(10, 10, 18);
}

.org-char-card--empty {
    border-color: rgb(102, 102, 102);
    opacity: 0.5;
}

.org-portrait-area {
    width: 160px;
    height: 160px;
    overflow: hidden;
    background-color: rgb(10, 10, 18);
    align-items: center;
    justify-content: center;
    -unity-background-scale-mode: scale-and-crop;
    /* Unity USS does not support filter:brightness/contrast/sepia.
       Portrait darkening from the design is omitted. If desired,
       place an absolutely-positioned child VisualElement with
       background-color: rgba(0,0,0,0.3) over the portrait. */
}

.org-portrait-area--empty {
    background-color: rgb(10, 10, 18);
}

.org-info-block {
    padding: 4px 5px;
    flex-direction: column;
    background-color: rgb(18, 16, 26);
    border-top-width: 1px;
    border-color: rgb(106, 80, 16);
}

.org-char-name {
    font-size: 16px;
    -unity-font-style: bold;
    color: rgb(212, 168, 64);
    white-space: normal;
    -unity-font: url("project://database/Assets/UI/Fonts/Cinzel-Bold.ttf?fileID=12800000&guid=f580297c1d9f7c74984732caed69c108&type=3#Cinzel-Bold");
}

.org-char-role {
    font-size: 14px;
    -unity-font-style: italic;
    color: rgb(136, 102, 64);
    -unity-font: url("project://database/Assets/UI/Fonts/IMFellEnglish-Italic.ttf?fileID=12800000&guid=ecf34c4fafdd2a546abecbc8943f73bb&type=3#IMFellEnglish-Italic");
}

.org-char-stats {
    flex-direction: row;
    flex-wrap: wrap;
    margin-top: 2px;
}

/* Reuses .char-stat-chip and .char-stat-icon from above — same structure */
```

---

## Step 4 — OrgActions.uss: action card structural redesign

**File:** `Assets/UI/Overlay/OrgInfo/OrgActions.uss`

Replace the existing `.action-card`, `.action-card--available`, `.action-card--unavailable`, `.action-card-name`, `.action-card-image`, `.action-card-price`, `.action-card-price--affordable`, `.action-card-price--unaffordable` rules with the new structured layout. The wrapper and section classes (`.org-actions-root`, `.hand-section`, `.hand-container`, `.card-lift-wrapper`, `.action-deck-wrapper`, `.action-card-play-btn`) are unchanged.

New/replacement rules:

```css
/* Card shell — now a plain column with no internal padding;
   sub-sections own their own padding */
.action-card {
    width: 240px;
    min-height: 300px;
    flex-direction: column;
    border-width: 2px;
    border-radius: 4px;
    overflow: hidden;
    background-color: rgb(212, 196, 160);
}

/* Available: gold border + soft blue glow (box-shadow not supported in Unity USS;
   the border colour change is the available signal) */
.action-card--available {
    border-color: rgb(136, 204, 255);
}

/* Unavailable: parchment with reduced opacity */
.action-card--unavailable {
    border-color: rgb(139, 109, 56);
    opacity: 0.55;
}

/* Success/Fail result states — applied by CardPlayAnimator */
.action-card--success {
    border-color: rgb(40, 168, 74);
}

.action-card--fail {
    border-color: rgb(224, 64, 64);
}

/* Header bar (navy, Cinzel bold) */
.action-card-header {
    background-color: rgb(26, 42, 72);
    color: rgb(240, 232, 208);
    font-size: 20px;
    -unity-font-style: bold;
    -unity-text-align: middle-center;
    padding: 4px 6px;
    border-bottom-width: 1px;
    border-color: rgb(200, 160, 64);
    white-space: normal;
    -unity-font: url("project://database/Assets/UI/Fonts/Cinzel-Bold.ttf?fileID=12800000&guid=f580297c1d9f7c74984732caed69c108&type=3#Cinzel-Bold");
}

/* Art area */
.action-card-art {
    width: 100%;
    height: 160px;
    overflow: hidden;
    background-color: rgb(106, 136, 152);
    align-items: center;
    justify-content: center;
    -unity-background-scale-mode: scale-and-crop;
}

/* Body: description + success % */
.action-card-body {
    padding: 5px 6px;
    flex: 1;
    flex-direction: column;
}

.action-card-desc {
    font-size: 18px;
    color: rgb(58, 40, 16);
    white-space: normal;
    -unity-font: url("project://database/Assets/UI/Fonts/IMFellEnglish-Regular.ttf?fileID=12800000&guid=1130d97d7eaf22b47b5ca1eb6354def6&type=3#IMFellEnglish-Regular");
}

.action-card-success-pct {
    font-size: 18px;
    color: rgb(136, 136, 168);
    -unity-font: url("project://database/Assets/UI/Fonts/IMFellEnglish-Regular.ttf?fileID=12800000&guid=1130d97d7eaf22b47b5ca1eb6354def6&type=3#IMFellEnglish-Regular");
}

/* Cost row */
.action-card-cost {
    border-top-width: 1px;
    border-color: rgb(184, 160, 120);
    padding: 3px 6px;
    flex-direction: row;
    align-items: center;
    background-color: rgb(196, 182, 148);
}

.action-card-cost-label {
    font-size: 18px;
    color: rgb(42, 26, 8);
    margin-left: 4px;
    -unity-font: url("project://database/Assets/UI/Fonts/Cinzel-Bold.ttf?fileID=12800000&guid=f580297c1d9f7c74984732caed69c108&type=3#Cinzel-Bold");
}

.action-card-cost-label--unaffordable {
    color: rgb(224, 64, 64);
}

/* Cost icon (coin PNG, 18px, gold tint) */
.action-card-cost-icon {
    width: 18px;
    height: 18px;
    -unity-background-scale-mode: scale-to-fit;
    -unity-background-image-tint-color: rgb(200, 160, 64);
    background-image: url("project://database/Assets/UI/Icons/coin.png?fileID=2800000&guid=46e003f67ebde7347a8004dd120d478a&type=3#coin");
}
```

Remove (delete from the file): `.action-card-name`, `.action-card-image`, `.action-card-price`, `.action-card-price--affordable`, `.action-card-price--unaffordable`, and `.action-card-play-btn` (no C# code creates this class — it is already dead code). These are replaced by the new sub-elements above.

---

## Step 5 — OrgInfo.uss: org-bar icon button styles

**Pre-condition (do first):** `characters.png` and `actions.png` have no `.meta` files yet. Import both before writing any CSS:

```
manage_asset(action="import", path="Assets/UI/Icons/characters.png")
manage_asset(action="import", path="Assets/UI/Icons/actions.png")
```

Then read `Assets/UI/Icons/characters.png.meta` and `Assets/UI/Icons/actions.png.meta` to obtain the GUIDs, and substitute them into the rules below. The same applies to `skill.png` (also untracked — import and read its `.meta` if Step 1's convenience class for it is needed).

**File:** `Assets/UI/Overlay/OrgInfo/OrgInfo.uss`

Add new rules for icon buttons (used on `chars-toggle-btn` and `actions-toggle-btn`):

```css
/* Icon button (icon left of label) */
.org-toggle-btn-icon {
    width: 20px;
    height: 20px;
    -unity-background-scale-mode: scale-to-fit;
    margin-right: 6px;
}

.org-toggle-btn-icon--characters {
    /* GUID obtained from Assets/UI/Icons/characters.png.meta */
    background-image: url("project://database/Assets/UI/Icons/characters.png?fileID=2800000&guid=CHARACTERS_PNG_GUID&type=3#characters");
    -unity-background-image-tint-color: rgb(240, 232, 208);
}

.org-toggle-btn-icon--actions {
    /* GUID obtained from Assets/UI/Icons/actions.png.meta */
    background-image: url("project://database/Assets/UI/Icons/actions.png?fileID=2800000&guid=ACTIONS_PNG_GUID&type=3#actions");
    -unity-background-image-tint-color: rgb(200, 160, 64);
}
```

During implementation, replace `CHARACTERS_PNG_GUID` and `ACTIONS_PNG_GUID` with the actual GUID values from the respective `.png.meta` files. The files exist (`Assets/UI/Icons/characters.png`, `Assets/UI/Icons/actions.png`) but their `.meta` files are not yet committed — check after first Unity import or inspect the meta in the working copy.

Also update `.chars-toggle-btn` in **SharedStyles.uss** (not OrgInfo.uss) to set flex layout for icon + label. This class is also used in `CountryInfo.uxml`, which only imports `SharedStyles.uss` — placing the rule in `OrgInfo.uss` would silently leave that panel's button broken. Replace the existing empty `.chars-toggle-btn {}` block in `SharedStyles.uss`:

```css
/* In SharedStyles.uss — replace the existing empty .chars-toggle-btn {} block */
.chars-toggle-btn {
    flex-direction: row;
    align-items: center;
    justify-content: center;
}
```

---

## Step 6 — OrgInfo.uxml: add icon elements to toggle buttons

**File:** `Assets/UI/Overlay/OrgInfo/OrgInfo.uxml`

Replace the two `<ui:Button>` elements in `org-toggle-block` with versions that contain icon children:

```xml
<ui:VisualElement class="org-toggle-block">
    <ui:Button name="chars-toggle-btn" class="chars-toggle-btn gs-btn gs-btn--small">
        <ui:VisualElement class="org-toggle-btn-icon org-toggle-btn-icon--characters" picking-mode="Ignore" />
        <ui:Label text="Characters &#x25B2;" picking-mode="Ignore" />
    </ui:Button>
    <ui:Button name="actions-toggle-btn" class="gs-btn gs-btn--small">
        <ui:VisualElement class="org-toggle-btn-icon org-toggle-btn-icon--actions" picking-mode="Ignore" />
        <ui:Label text="Actions &#x25B2;" picking-mode="Ignore" />
    </ui:Button>
</ui:VisualElement>
```

The child `<ui:Label>` inside a `<ui:Button>` replaces the `text=""` attribute. This is the standard approach for adding icon + label layouts in UI Toolkit when you need both elements inside the button's flex row.

Note: the `▲` glyph (&#x25B2;) is toggled to `▼` (&#x25BC;) in C# when the panel opens. `OrgInfoDocument.cs` currently sets `button.text = "▲ Characters"` — after this change it should set the child `<ui:Label>`'s text or toggle a CSS class. See Step 10 for the C# change.

---

## Step 7 — C# OrgActionsView: rebuild card DOM

**File:** `Assets/Scripts/Unity/UI/OrgActionsView.cs`

### 7a. `BuildHandCard()` — new DOM structure

Replace the existing element-building sequence in `BuildHandCard()`. The new structure is:

```
wrapper (card-lift-wrapper)
  └── cardEl (action-card + action-card--available/unavailable)
        ├── header (action-card-header) [Label with action name]
        ├── art (action-card-art)       [background-image = sprite]
        ├── body (action-card-body)
        │     ├── desc (action-card-desc) [description text]
        │     └── pct  (action-card-success-pct) ["{N}% success"]
        └── cost (action-card-cost)      [only if def has prices]
              ├── icon (action-card-cost-icon)
              └── label (action-card-cost-label + optional --unaffordable)
```

Key C# changes:
- Remove: `nameLabel` with class `action-card-name`, `img` with class `action-card-image`, `priceLabel` with class `action-card-price/price--affordable/price--unaffordable`.
- Add `header` label (class `action-card-header`) containing the localised action name.
- Add `art` VisualElement (class `action-card-art`). Apply `backgroundImage` to the art element (same sprite as before).
- Add `body` VisualElement (class `action-card-body`). Add a `desc` Label (class `action-card-desc`) from `def.DescKey` if `def != null`. Add a `pct` Label (class `action-card-success-pct`) formatted as `$"{(int)(def.SuccessRate * 100)}% success"`.
- Add `cost` VisualElement (class `action-card-cost`) only when `def != null && def.Prices.Count > 0`. Inside: icon VisualElement (class `action-card-cost-icon`) + label Label (class `action-card-cost-label` + `action-card-cost-label--unaffordable` when `!canAfford`) containing `$"{price.Amount:F1} {resName}"` for all prices (or format as a single combined string if multiple prices).
- The `PointerUpEvent` callback stays on `cardEl` (unchanged pattern, same canAfford guard).
- The tooltip registration on `wrapper` stays (unchanged).

### 7b. `BuildDeckPile()` — deck back cards

The deck pile renders back-of-card images on plain `action-card` elements. Because the back image covers the entire card, the internal sub-structure (header/art/body/cost) is invisible. No structural DOM change is needed for deck cards — the existing approach of setting `backgroundImage` with `BackgroundSizeType.Cover` on a bare `action-card` element is sufficient. Leave `BuildDeckPile()` unchanged structurally.

---

## Step 8 — C# CharactersView: rebuild country character cards

**File:** `Assets/Scripts/Unity/UI/CharactersView.cs`

Replace `BuildCharacterCard()` to produce the new `.char-card` structure:

```
card (char-card)
  ├── portrait (char-portrait-area) [backgroundImage = sprite OR show placeholder bg]
  └── info (char-info)
        ├── nameLabel (char-name)
        ├── roleLabel (char-role)   [localised role name]
        └── statsBlock (char-stats)
              └── per-skill: chip (char-stat-chip)
                    ├── icon (char-stat-icon + skill-specific bg and tint class)
                    └── value Label (char-stat-value, plain text)
```

Key C# changes:
- Remove: old `character-card`, `character-name`, `character-portrait`, `role-block`, `character-role-icon--*`, `skills-block`, `skill-chip`, `character-skill-icon--*`, `skill-value` classes.
- Card root: class `char-card`.
- Portrait element: class `char-portrait-area`. If sprite is available set `backgroundImage`. If no sprite: leave background-color from USS (`rgb(106, 136, 152)`) as the placeholder. Remove the `DisplayStyle.None` hide — the area is always visible.
- Info block: class `char-info`. Contains:
  - Name label: class `char-name`, text from name-part keys joined with spaces.
  - Role label: class `char-role`, text = localised role name.
  - Stats block: class `char-stats`. For each skill chip: class `char-stat-chip`. Skill icon VisualElement: class `char-stat-icon` plus the appropriate tint class from SharedStyles (e.g. `gs-icon--tint-gold` for power, derive mapping from skill ID). Value label: plain text, font size handled by the parent chip's inherited style (or add a small `.char-stat-value` class with `font-size: 14px; color: rgb(200, 184, 144)`).
- Tooltip registration on the chip row stays (unchanged pattern).
- Remove the separate `roleBlock` tooltip — wrap the role label in a tooltipped element if still desired.

Skill-to-tint mapping (add as a `static` helper or a `switch`):
- `power` → `gs-icon--tint-mil` (purple)
- `charm` → `gs-icon--tint-dip` (green)
- `stinginess` → `gs-icon--tint-gold` (gold)
- `intrigue` → `gs-icon--tint-sec` (teal)

The existing `character-skill-icon--{skillId}` rules in `SharedStyles.uss` currently set `width` (28px), `height` (28px), `margin-right`, and `-unity-background-image-tint-color` — all of which conflict with the new `char-stat-icon` (14px). They must be updated: strip `width`, `height`, `margin-right`, and `-unity-background-image-tint-color` from each rule, leaving only `background-image`. This makes each class a pure image provider that the new size and tint classes can safely layer on top:

```css
/* Updated in SharedStyles.uss — size/tint removed, only background-image remains */
.character-skill-icon--power     { background-image: url("...lightning-fill.svg..."); }
.character-skill-icon--charm     { background-image: url("...chat-heart-fill.svg..."); }
.character-skill-icon--stinginess { background-image: url("...coin.svg..."); }
.character-skill-icon--intrigue  { background-image: url("...eye-slash-fill.svg..."); }
```

Each chip element then adds three classes: `char-stat-icon` (14px size), the appropriate `gs-icon--tint-*` class (tint colour), and `character-skill-icon--{id}` (background-image only).

---

## Step 9 — C# OrgCharactersView: rebuild org character cards

**File:** `Assets/Scripts/Unity/UI/OrgCharactersView.cs`

Replace `BuildFilledCard()` and `BuildEmptyCard()` to use the new `.org-char-card` structure.

### `BuildFilledCard()`

```
card (org-char-card)
  ├── portrait (org-portrait-area)  [backgroundImage = sprite]
  └── infoBlock (org-info-block)
        ├── nameLabel (org-char-name)
        ├── roleLabel (org-char-role)
        └── statsBlock (org-char-stats)
              └── per-skill: chip (char-stat-chip)
                    ├── icon (char-stat-icon + tint class)
                    └── value Label
```

Same tint mapping and tooltip pattern as CharactersView.

### `BuildEmptyCard()`

```
card (org-char-card + org-char-card--empty)
  ├── portrait (org-portrait-area org-portrait-area--empty)
  │     └── [optional: agent icon at low opacity for slot-type hint]
  └── infoBlock (org-info-block)
        ├── roleLabel (org-char-role) [role name, e.g. "Master"]
        └── statusLabel (gs-hint) [localised "Available" or "Empty"]
```

Remove: `character-card`, `character-card--empty`, `character-portrait`, `character-portrait--empty`, `character-slot-status`, `character-slot-status--available`, `character-slot-status--empty` class usage in this file.

---

## Step 10 — C# CardPlayAnimator: update `PopulateTestCard()` + success/fail card border

**File:** `Assets/Scripts/Unity/UI/CardPlayAnimator.cs`

### 10a. `PopulateTestCard()`

Replace the existing implementation. `cardSlot` is the `card-test-card` element, which should already have `.action-card` on it (or the animator should add it). Build the same sub-structure as `BuildHandCard()` from `OrgActionsView`:

```csharp
void PopulateTestCard(VisualElement cardSlot, string actionId) {
    cardSlot.Clear();
    // action-card and action-card--available are already set in HUD.uxml.
    // Reset any lingering result-state classes from a prior play.
    cardSlot.RemoveFromClassList("action-card--success");
    cardSlot.RemoveFromClassList("action-card--fail");

    var def = _actionConfig?.Find(actionId);
    string name = def != null ? _loc.Get(def.NameKey) : actionId;

    var header = new Label(name);
    header.AddToClassList("action-card-header");
    cardSlot.Add(header);

    var art = new VisualElement();
    art.AddToClassList("action-card-art");
    var sprite = _visualConfig?.FindFront(actionId);
    if (sprite != null) {
        art.style.backgroundImage = new StyleBackground(sprite);
    }
    cardSlot.Add(art);

    var body = new VisualElement();
    body.AddToClassList("action-card-body");
    if (def != null) {
        var desc = new Label(_loc.Get(def.DescKey));
        desc.AddToClassList("action-card-desc");
        body.Add(desc);
        var pct = new Label($"{(int)(def.SuccessRate * 100)}% success");
        pct.AddToClassList("action-card-success-pct");
        body.Add(pct);
    }
    cardSlot.Add(body);

    if (def != null && def.Prices.Count > 0) {
        var cost = new VisualElement();
        cost.AddToClassList("action-card-cost");
        var costIcon = new VisualElement();
        costIcon.AddToClassList("action-card-cost-icon");
        cost.Add(costIcon);
        foreach (var price in def.Prices) {
            var costLabel = new Label($"{price.Amount:F1}");
            costLabel.AddToClassList("action-card-cost-label");
            cost.Add(costLabel);
        }
        cardSlot.Add(cost);
    }
}
```

### 10b. Success/Fail visual feedback on the test card

After the roll result is known, update the `card-test-card` border colour. `action-card--available` is baked into the UXML element, so explicitly remove it first to prevent it from conflicting with the result state border:

```csharp
if (cardTestCard != null) {
    cardTestCard.RemoveFromClassList("action-card--available");
    cardTestCard.EnableInClassList("action-card--success", success);
    cardTestCard.EnableInClassList("action-card--fail", !success);
}
```

The `roll-result-label` text update can remain for accessibility. Leave the existing label text change in place to keep it visible.

### 10c. Remove stale `action-card-name` / `action-card-image` class references

After `PopulateTestCard()` is updated, there are no remaining references to the removed CSS classes. Check via grep before closing the step.

---

## Step 11 — C# OrgInfoDocument: update toggle-button label toggling

**File:** `Assets/Scripts/Unity/UI/OrgInfoDocument.cs`

The buttons now contain a child `<ui:Label>` for the text (arrow + name) rather than using `Button.text`. Update the code that flips the arrow character when panels open/close:

- After the UXML change (Step 6), `button.text = "▲ Characters"` will set the button's own text, overwriting the child elements. Instead, query the child label: `button.Q<Label>().text = "▼ Characters ▲"` — or use a `gs-btn--active` class toggle for the open state and let USS handle the visual, keeping the arrow flipping via the child label text.
- Verify: search `OrgInfoDocument.cs` for assignments to `.text` on `chars-toggle-btn` and `actions-toggle-btn` and update each one to target the child label.

---

## Step 12 — Verify USS scope and fix HUD.uxml inline style

### 12a. Remove inline `min-height` from `card-test-card` in HUD.uxml

**File:** `Assets/UI/HUD/HUD.uxml`

The `card-test-card` element has an inline `min-height: 320px` which overrides the new USS `min-height: 300px` rule (inline styles beat USS). This causes a visible size mismatch between the test-card overlay and hand cards during the animation transition. Remove only the `min-height` from the inline style; keep `width` and `margin-right`:

```xml
<!-- Before -->
<ui:VisualElement name="card-test-card" class="action-card action-card--available"
    style="width: 240px; min-height: 320px; margin-right: 80px;" />

<!-- After -->
<ui:VisualElement name="card-test-card" class="action-card action-card--available"
    style="width: 240px; margin-right: 80px;" />
```

### 12b. Verify USS imports

After Step 4 adds new classes to `OrgActions.uss` and Step 3 adds org-char-card classes to `SharedStyles.uss`:

- `OrgInfo.uxml` already imports both `SharedStyles.uss` and `OrgActions.uss`. The new classes in those files are available to all elements under `org-info-root` including dynamically created ones.
- `CharactersView` elements are added to the `characters-container` inside `OrgInfo.uxml`. New `.char-card` classes are in `SharedStyles.uss` which is already imported. No additional import needed.
- `OrgCharactersView` elements are added to the same document. New `.org-char-card` classes are in `SharedStyles.uss`. No additional import needed.
- `CardPlayAnimator` adds elements to the HUD document (`_hudDocument`). `HUD.uxml` already imports `OrgActions.uss` (confirmed). No additional import needed.

---

## Checklist (ordered by dependency)

1. `SharedStyles.uss` — icon tint classes (Step 1)
2. `SharedStyles.uss` — `.char-card` family (Step 2)
3. `SharedStyles.uss` — `.org-char-card` family (Step 3)
4. `OrgActions.uss` — replace action card rules (Step 4)
5. `OrgInfo.uss` — icon button styles (Step 5, import PNGs first per pre-condition)
6. `OrgInfo.uxml` — icon children in toggle buttons (Step 6)
7. `OrgInfoDocument.cs` — update toggle label text targeting (Step 11) **← do immediately after Step 6, before any refresh**
8. `OrgActionsView.cs` — rebuild `BuildHandCard()` DOM (Step 7)
9. `CharactersView.cs` — rebuild `BuildCharacterCard()` (Step 8)
10. `OrgCharactersView.cs` — rebuild both card builders (Step 9)
11. `CardPlayAnimator.cs` — update `PopulateTestCard()` and add result border classes (Step 10)
12. `HUD.uxml` — remove inline `min-height` from `card-test-card` (Step 12 addition, see below)
13. USS scope verification — HUD document imports (Step 12)
14. After each C# change: `refresh_unity` + `read_console(types=["error"])` before proceeding

---

## Unity 6 Limitations Notes

- **`filter:`** not supported in USS — org portrait darkening from the design is skipped or approximated with a tint overlay child element.
- **`box-shadow:`** not supported in USS — the blue/green/red card glow effects from the design are replaced with border-colour changes only.
- **`border-style: dashed`** not supported — not used in this design, no workaround needed.
- **`Button.clicked`** is broken — all interactive elements already use `PointerUpEvent` with bounds check (no change needed, existing pattern is correct).
- **`rgba()` with fractional alpha** in USS — use integer 0–255 range or a solid approximation.
- **`gap:` shorthand in UXML inline style** may not parse — all gap/spacing uses USS classes, not inline styles.

---

Use /implement to start working on the plan or request changes.
