namespace GS.Unity.UI {
	public interface IFlyTextNotifier {
		void Notify(string localizationKey, params object[] args);
		void NotifyRaw(string text);
	}
}
