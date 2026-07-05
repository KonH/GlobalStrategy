namespace GS.Main {
	public interface IGameLogger {
		void LogError(string message);
		void LogInfo(string message);
		void LogDebug(string message);
	}
}
