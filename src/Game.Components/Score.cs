namespace GS.Game.Components {
	// Not [Savable] — fully derivable from control + country_score at any point in time;
	// recomputed daily by OrgScoreSystem for Organization entities. Country no longer
	// composes Score (see .claude/rules/unity/ecs_patterns.md's "Country + Score" update,
	// Docs/Specs/26_07_18_17_resource-collector-pipeline).
	public struct Score {
		public double Value;
	}
}
