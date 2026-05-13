using GS.Main;
using UnityEngine;

namespace GS.Unity.DI {
	sealed class UnityGameLogger : IGameLogger {
		public void LogError(string message) => Debug.LogError(message);
	}
}
