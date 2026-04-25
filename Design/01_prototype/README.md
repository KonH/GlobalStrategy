# UI Design Prototype

Browser-based design workspace for iterating on the Global Strategy UI before touching Unity.

## Files

| File | Purpose |
|------|---------|
| `design-final.html` | **Start here.** Final approved design — each mockup paired with its original in-engine screenshot. |
| `design-preview.html` | Full iteration history — palette A/B/C/D comparison, font comparison, progressive polishing. |
| `fonts/` | TTF files ready for Unity Font Asset import. |
| `icons/` | SVG icons (Bootstrap Icons, MIT) used in HUD buttons and gold counter. |
| `initial/` | Original in-engine screenshots used as baseline reference. |

## How to read design-final.html

Open in any browser (no server needed — all paths are relative).

Each screen section shows:
- **Header** — screen name + thumbnail(s) of the original screenshot. Click a thumbnail to open the full screenshot in a new tab.
- **Mockup** — the approved design for that screen.

The file uses a single CSS palette block (`data-p="navy"`) with custom properties, matching what will be translated into `Assets/UI/Shared/SharedStyles.uss`.

## How to read design-preview.html

Four iterations, each building on the previous:

| Iteration | What it tests |
|-----------|--------------|
| **0** | Original screenshots — baseline only, no changes |
| **1** | Four palette options (A current · B aged · C slate · **D navy ✓**) |
| **2** | Four HUD font options with D palette (**D2 Cinzel/IM Fell ✓** · D1/D3/D4 alternatives) |
| **3** | D2 polished — 17 px date/gold, settings grid, time block states |
| **4** | Icons replacing text symbols (||, >, III, $) with SVGs |

The selected option in each iteration is marked with a green border.

## Core UI Principles

### Palette — Navy
Dark navy panels (`#1A2A48`) with gold borders (`#C8A040`) convey a late 19th-century map-room aesthetic. Light cream text (`#F0E8D0`) on dark backgrounds gives strong contrast without feeling modern.

| Token | Value | Used for |
|-------|-------|---------|
| Panel bg | `#1A2A48` | All panels and HUD boxes |
| Border | `#C8A040` | All panel/button borders |
| Button bg | `#243258` | Normal button fill |
| Button active | `#C8A040` | Selected/active state (gold fill) |
| Speed active | `#1E5030` / `#90FFB8` | Currently-running speed button (green) |
| Text dark | `#F0E8D0` | Primary text, headers |
| Text mid | `#C8B890` | Labels, gold counter, date |
| Text hint | `#8888A8` | Italic hints, secondary info |
| Positive | `#50D870` | Income, buffs |
| Negative | `#E04040` | Costs, debuffs |

### Fonts

| Font | Use | Files |
|------|-----|-------|
| **Cinzel** | Titles (`gs-title`, `gs-header`), HUD speed/menu buttons | `Cinzel-Regular.ttf`, `Cinzel-Bold.ttf` |
| **IM Fell English** | Body text, labels, gold counter, date | `IMFellEnglish-Regular.ttf`, `IMFellEnglish-Italic.ttf` |
| **Playfair Display** | Cyrillic fallback (RU locale) | `PlayfairDisplay-Regular.ttf`, `PlayfairDisplay-Bold.ttf` |

Cinzel is all-caps — X1/X2/X3 and pause/play icons sit at consistent cap-height in HUD buttons.
IM Fell English italic is used for tooltip hints.
Playfair Display is set as a Unity font asset fallback chain behind Cinzel/IM Fell for any Cyrillic characters.

### Icons

Sourced from **Bootstrap Icons** (MIT licence). Stored as SVG in `icons/`.

| Icon | File | Replaces |
|------|------|---------|
| Coin / gold | `coin.svg` | `$ ` prefix on gold counter |
| Pause | `pause.svg` | `\|\|` text label |
| Play | `play.svg` | `>` text label (shown when paused) |
| Menu | `menu.svg` | `III` text label |

Icons are black SVGs tinted in CSS via `filter: brightness(0) invert(...)` chains. In Unity, import as `Texture2D` (Sprite mode) and apply as `background-image` in USS, with `-unity-background-image-tint-color` for tinting.

### Layout rules

- **HUD panels** — absolute-positioned, `top:6px left:6px` (country) and `top:6px right:6px` (time controls). Bottom bar spans full width.
- **Modal panels** — centered over map background, fixed width (125–228 px depending on content).
- **Select Country panel** — `position:absolute; bottom:6px; right:6px` inside a `min-height:122px` container. The hint text sits above the disabled Start Game button.
- **Settings** — two-column CSS grid (`auto 1fr`) so toggle groups always left-align regardless of label length.
- **Tooltips** — dark navy background distinguishes them from panels. Locked (pinned) state uses a dashed border. Second-layer tooltip adds a `<hr>` separator and a detail breakdown below.
