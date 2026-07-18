# GlobalStrategy

## Tech Stack
- **Engine:** Unity 6000.4.1f1
- **Language:** C#

## Shell

- The shell starts in the project root — never use `cd` before git commands, run them directly
- Never chain shell commands with `&&` — run each as a separate Bash tool call

## Configuration Index
- **Workflow & tool usage:** `.Codex/rules/workflow.md`
- **Commit rules:** `.Codex/commands/commit.md`
- **Specify command:** `.Codex/commands/specify.md` — creates `Docs/Specs/<YY_MM_DD_HH>_<name>/spec.md` before planning; feature work starts here
- **Plan command:** `.Codex/commands/plan.md` — saves plans to `Docs/Specs/<YY_MM_DD_HH>_<name>/plan.md`, whether or not a `spec.md` accompanies them (technical-only plans use the same subdirectory format, just without a spec); `Docs/Plans/<index>_<name>.md` is legacy, kept only for existing entries
- **Constitution:** `Docs/Constitution.md` — non-negotiable architectural principles; checked by the plan command before finalising any plan
- **Specs directory:** `Docs/Specs/` — home for spec+plan pairs produced by `/specify` + `/plan`
- **C# code style:** `.Codex/rules/csharp/code_style.md`
- **Unity project structure:** `.Codex/rules/unity/project_structure.md`
- **Unity asmdef format:** `.Codex/rules/unity/asmdef.md`
- **Unity prefabs:** `.Codex/rules/unity/prefabs.md`
- **Unity UI implementation:** `.Codex/rules/unity/ui_implementation.md`
- **Unity scenes:** `.Codex/rules/unity/scenes.md`
- **Unity MCP usage:** `.Codex/rules/unity/mcp_usage.md`
- **Map system architecture:** `.Codex/rules/unity/map_system.md`
- **Map config generator:** `.Codex/rules/unity/map_config_generator.md`
- **Province config generator:** `.Codex/rules/unity/province_config_generator.md`
- **UI Toolkit architecture:** `.Codex/rules/unity/uitoolkit.md`
- **VContainer / DI:** `.Codex/rules/unity/vcontainer.md`
- **Unity plugins (DLLs):** `.Codex/rules/unity/plugins.md`
- **Unity input handling:** `.Codex/rules/unity/input.md`
- **ECS patterns:** `.Codex/rules/unity/ecs_patterns.md`
- **Unity Editor scripts:** `.Codex/rules/unity/editor_scripts.md`
- **Localization system:** `.Codex/rules/unity/localization.md`
- **Unity WebGL gotchas:** `.Codex/rules/unity/webgl.md`
- **Game loop integration from UI:** `.Codex/rules/unity/game_loop_integration.md`
- **Animation barriers:** `.Codex/rules/animation_barriers.md`
- **Flag & org image assets:** `.Codex/rules/flag_assets.md`
- **Temporary scripts:** `.Codex/rules/temp_scripts.md`
- **Learning workflow:** `.Codex/commands/learn.md`
