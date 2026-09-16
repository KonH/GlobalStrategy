namespace GS.Game.Configs {
	// Derives display-text keys, effect-text keys and icon class names from a resource/effect
	// identifier verbatim - no case or separator transformation. Resource ids are [Savable]
	// (Resource.ResourceId, ResourceLink.ResourceId), appear across multiple config files and
	// the console runner, so the id's own spelling is the only spelling used anywhere.
	public static class ResourceDisplayNaming {
		public static string NameKey(string resourceId) {
			return $"resource.{resourceId}.name";
		}

		public static string DescriptionKey(string resourceId) {
			return $"resource.{resourceId}.description";
		}

		public static string EffectNameKey(string effectId) {
			return $"effect.{effectId}.name";
		}

		// Derives ".description", which is scoped to ResourceConfig.EffectDefinition only.
		// Must NOT be reused for ActionEffectDefinition (src/Game.Configs/EffectConfig.cs,
		// backed by effects.json), which uses ".desc" instead.
		public static string EffectDescriptionKey(string effectId) {
			return $"effect.{effectId}.description";
		}

		public static string IconClass(string resourceId) {
			return $"resource-icon--{resourceId}";
		}
	}
}
