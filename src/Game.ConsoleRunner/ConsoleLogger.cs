using System;
using GS.Main;

namespace GS.Game.ConsoleRunner {
	sealed class ConsoleLogger : IGameLogger {
		public void LogError(string message) => Console.Error.WriteLine($"[ERROR] {message}");
		public void LogInfo(string message) => Console.WriteLine($"[INFO] {message}");
		public void LogDebug(string message) => Console.WriteLine($"[DEBUG] {message}");
	}
}
