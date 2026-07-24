using System;
using GS.Main;

namespace GS.Game.WebClient.Services {
	// Blazor WASM's Console.WriteLine/Error surface in the browser dev console, same
	// as Game.ConsoleRunner.ConsoleLogger's role for the console client.
	public class ConsoleGameLogger : IGameLogger {
		public void LogError(string message) => Console.Error.WriteLine($"[ERROR] {message}");
		public void LogInfo(string message) => Console.WriteLine($"[INFO] {message}");
		public void LogDebug(string message) => Console.WriteLine($"[DEBUG] {message}");
	}
}
