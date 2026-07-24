using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GS.Game.WebClient.LocaleTool {
	public static class Program {
		public static int Main(string[] args) {
			if (args.Length < 4) {
				Console.Error.WriteLine(
					"Usage: Game.WebClient.LocaleTool <enAssetPath> <ruAssetPath> <enOutJsonPath> <ruOutJsonPath>");
				return 1;
			}

			WriteLocaleJson(args[0], args[2]);
			WriteLocaleJson(args[1], args[3]);
			return 0;
		}

		static void WriteLocaleJson(string assetPath, string outJsonPath) {
			string content = File.ReadAllText(assetPath);
			Dictionary<string, string> entries = LocaleAssetParser.Parse(content);

			string? directory = Path.GetDirectoryName(outJsonPath);
			if (!string.IsNullOrEmpty(directory)) {
				Directory.CreateDirectory(directory);
			}

			string json = JsonSerializer.Serialize(entries);
			File.WriteAllText(outJsonPath, json);
		}
	}
}
