Create a plan for the requested task, using the shared `k:plan` skill.
The only project-specific override is what the "User Steps" section should call out.

## Step Block wording override

In the "User Steps" section of the plan, "requires manual interaction Claude cannot perform" specifically means: Unity Editor scene/asset work, visual inspection in the Editor, or other hands-on Unity steps.

## Delegate

Invoke the `k:plan` skill (from the `k` plugin) with the override above. It handles index derivation, the constitution gate (`Docs/Constitution.md`), spec detection, architect sub-agent, and `plan-review` hand-off.
