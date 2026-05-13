using System;
using GS.Main;

namespace GS.Game.ConsoleRunner {
	sealed class ConsoleLogger : IGameLogger {
		public void LogError(string message) => Console.Error.WriteLine($"[ERROR] {message}");
	}
}
