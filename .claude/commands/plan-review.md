Review the latest plan, using the shared `cc:plan-review` skill. The only project-specific addition is which rule categories to check.

## Rule categories override

When the skill's sub-agent reads "relevant project rules for the plan's scope," that means Unity, ECS, UI, and C# rules under `.claude/rules/` for this project specifically.

## Delegate

Invoke the `cc:plan-review` skill (from the `cc` plugin) with the rule categories above. It handles plan discovery, spawning the review sub-agent, and presenting concerns.
