# GlobalStrategy

## Tech Stack
- **Engine:** Unity 6000.4.1f1
- **Language:** C#

## Shell

- The shell starts in the project root — never use `cd` before git commands, run them directly
- Never chain shell commands with `&&` — run each as a separate Bash tool call

## Configuration Index
- **Workflow & tool usage:** `.claude/rules/workflow.md`
- **Commit rules:** `.claude/commands/commit.md`
- **Specify command:** `.claude/commands/specify.md` — creates `Docs/Specs/<YY_MM_DD_HH>_<name>/spec.md` before planning; feature work starts here
- **Plan command:** `.claude/commands/plan.md` — saves plans to `Docs/Specs/<YY_MM_DD_HH>_<name>/plan.md`, whether or not a `spec.md` accompanies them (technical-only plans use the same subdirectory format, just without a spec); `Docs/Plans/<index>_<name>.md` is legacy, kept only for existing entries
- **Constitution:** `Docs/Constitution.md` — non-negotiable architectural principles; checked by the plan command before finalising any plan
- **Specs directory:** `Docs/Specs/` — home for spec+plan pairs produced by `/specify` + `/plan`
- **C# code style:** `.claude/rules/csharp/code_style.md`
- **Unity project structure:** `.claude/rules/unity/project_structure.md`
- **Unity asmdef format:** `.claude/rules/unity/asmdef.md`
- **Unity prefabs:** `.claude/rules/unity/prefabs.md`
- **Unity UI implementation:** `.claude/rules/unity/ui_implementation.md`
- **Unity scenes:** `.claude/rules/unity/scenes.md`
- **Unity MCP usage:** `.claude/rules/unity/mcp_usage.md`
- **Map system architecture:** `.claude/rules/unity/map_system.md`
- **Map config generator:** `.claude/rules/unity/map_config_generator.md`
- **Province config generator:** `.claude/rules/unity/province_config_generator.md`
- **UI Toolkit architecture:** `.claude/rules/unity/uitoolkit.md`
- **VContainer / DI:** `.claude/rules/unity/vcontainer.md`
- **Unity plugins (DLLs):** `.claude/rules/unity/plugins.md`
- **Unity input handling:** `.claude/rules/unity/input.md`
- **ECS patterns:** `.claude/rules/unity/ecs_patterns.md`
- **Unity Editor scripts:** `.claude/rules/unity/editor_scripts.md`
- **Localization system:** `.claude/rules/unity/localization.md`
- **Unity WebGL gotchas:** `.claude/rules/unity/webgl.md`
- **Game loop integration from UI:** `.claude/rules/unity/game_loop_integration.md`
- **Animation barriers:** `.claude/rules/animation_barriers.md`
- **Flag & org image assets:** `.claude/rules/flag_assets.md`
- **Temporary scripts:** `.claude/rules/temp_scripts.md`
- **Learning workflow:** `.claude/commands/learn.md`
- **Codex issue automation:** `.codex/skills/codex-feature-issue/SKILL.md`
