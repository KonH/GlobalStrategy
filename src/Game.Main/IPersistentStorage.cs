using System.Collections.Generic;

namespace GS.Main {
	public interface IPersistentStorage {
		void Write(string relativePath, string content);
		string Read(string relativePath);
		bool Exists(string relativePath);
		void Delete(string relativePath);
		IReadOnlyList<string> List(string relativeDir);
	}
}
