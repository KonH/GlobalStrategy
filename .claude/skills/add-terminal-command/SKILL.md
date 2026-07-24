---
name: add-terminal-command
description: Add or change an ICommand type so the Game.WebClient debug terminal keeps working with zero web-client code changes — mandates ParamSuggestionAttribute annotations on domain-id parameters and the matching provider for genuinely new id kinds.
---

# add-terminal-command

The web client's debug terminal (`src/Game.WebClient/Terminal/`) discovers every
`ICommand` type in `src/Game.Commands/` via reflection (`CommandRegistry`) — a new
command ships with just a new build, no web-client code change required. Tab
completion, however, depends on every domain-id parameter being annotated so the
terminal knows what to suggest. This skill is the checklist for keeping that
contract intact whenever an `ICommand` type is added or changed.

## When this applies

- Adding a new `ICommand` record/struct to `src/Game.Commands/`.
- Adding a new field or record-positional property to an existing `ICommand` type.
- Changing a parameter's type on an existing `ICommand` type (e.g. `string` → `int`,
  or widening a closed string set).

## Checklist

1. **Every domain-id parameter gets a `ParamSuggestionAttribute`.** A "domain-id
   parameter" is any public field or record-positional property whose name ends in
   `Id`, or is named `Locale`/`Interval` (the two known non-`Id` exceptions already
   in the codebase). Pick the attribute from `src/Game.Commands/ParamSuggestion.cs`
   that matches what the id refers to:
   - `[CountryId]` — a country id (`CountryConfig.Countries`)
   - `[OrgId]` — an organization id (`OrganizationConfig.Organizations`)
   - `[ProvinceId]` — a province id (`ProvinceConfig.Provinces`)
   - `[ActionId]` — an action id (`ActionConfig.Actions`)
   - `[RoleId]` — a character role id (`CharacterConfig.Roles`)
   - `[CharacterOwnerId]` — a character owner, which can be either a country or an
     org id (union of both)
   - `[LocaleId]` — a locale code (closed set: `en`, `ru`)

   For record types, the attribute goes on the primary-constructor parameter with
   the `[property: ...]` target, matching every existing record command:

   ```csharp
   public record struct SelectCountryCommand([property: CountryId] string CountryId) : ICommand;
   ```

   For plain structs, the attribute goes directly on the public field:

   ```csharp
   public struct DebugChangeGoldCommand : ICommand {
       [OrgId] public string OrgId;
       public double Amount;
   }
   ```

2. **Use `[OneOf(...)]` for a closed literal set that isn't backed by a config.**
   `ChangeAutoSaveIntervalCommand.Interval` is the existing example — the set of
   valid intervals (`daily`/`monthly`/`yearly`) isn't a config-driven id kind, it's
   just fixed strings:

   ```csharp
   public record struct ChangeAutoSaveIntervalCommand(
       [property: OneOf("daily", "monthly", "yearly")] string Interval
   ) : ICommand;
   ```

3. **Enum-typed parameters need no attribute at all.** `SuggestionValueResolver`
   auto-suggests `Enum.GetNames` for any parameter whose CLR type is an enum,
   regardless of attribute. Don't add a `ParamSuggestionAttribute` to an enum field
   — it isn't necessary and there is no attribute for it.

4. **A parameter with no attribute and no enum type gets no suggestions — this is
   fine, not an error.** Plain `int`/`double`/`bool` parameters, or a genuinely
   free-form string (not a domain id), are expected to be typed manually. Don't
   force an attribute onto something that isn't actually a lookup id.

5. **If the parameter is a genuinely new *kind* of id** (not one of the seven
   existing `ParamSuggestionAttribute` subclasses), add both pieces together in the
   same change:
   - A new sealed attribute in `src/Game.Commands/ParamSuggestion.cs`, following the
     existing shape (`[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]`,
     derives from `ParamSuggestionAttribute`).
   - A matching `ISuggestionValueProvider` implementation in
     `src/Game.WebClient/Terminal/Suggestions/`, wired into `SuggestionValueResolver`
     (`src/Game.WebClient/Terminal/Suggestions/SuggestionValueResolver.cs`).
   Adding only the attribute without the provider means the terminal silently offers
   no suggestions for that parameter — not a build error, so it's easy to miss;
   don't ship one without the other.

6. **The convention test enforces the mandatory part.**
   `src/Game.Tests/ParamSuggestionAttributeTests.cs` (`EveryCommand_DomainIdMember_CarriesSuggestionAttribute`)
   reflects over every non-abstract `ICommand` type and fails the build if any
   public field/property named `*Id` (or `Locale`/`Interval`) lacks a
   `ParamSuggestionAttribute`. Forgetting step 1 fails this test, not silently —
   run `dotnet test src/GlobalStrategy.Core.sln` (or the `dotnet-test` skill) after
   adding a command to confirm it passes.

## What this skill does not cover

- Command *execution* semantics (what the command does once pushed) — that's
  ordinary `ICommand`/system work, unrelated to terminal discovery.
- Presentation-side rendering of suggestions in `Terminal.razor` — that's generic
  once `SuggestionValueResolver` returns the right list; no per-command UI work is
  ever needed.
