namespace GS.Unity.Common {
	public static class SceneTransitionArgs {
		public static string SaveNameToLoad;
		public static string InitialPlayerCountry;

		public static void Clear() {
			SaveNameToLoad = null;
			InitialPlayerCountry = null;
		}
	}
}
