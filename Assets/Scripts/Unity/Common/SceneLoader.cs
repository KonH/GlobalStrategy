using UnityEngine.SceneManagement;

namespace GS.Unity.Common {
	public class SceneLoader {
		public void LoadMainMenu() {
			SceneTransitionArgs.Clear();
			SceneManager.LoadScene("MainMenu");
		}

		public void LoadSelectCountry() {
			SceneTransitionArgs.Clear();
			SceneManager.LoadScene("CountrySelection");
		}

		public void LoadGame(string? saveName = null, string? playerCountryId = null) {
			SceneTransitionArgs.Clear();
			SceneTransitionArgs.SaveNameToLoad = saveName;
			SceneTransitionArgs.InitialPlayerCountry = playerCountryId;
			SceneManager.LoadScene("Map");
		}
	}
}
