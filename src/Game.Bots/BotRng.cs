using System;

namespace GS.Game.Bots {
	public static class BotRng {
		// FNV-1a 32-bit over the org id's UTF-16 code units:
		// hash = 2166136261; foreach (char c in orgId) { hash ^= c; hash *= 16777619; }
		// Deterministic across processes, platforms, and .NET versions —
		// string.GetHashCode is per-process-randomized and MUST NOT be used.
		public static int DeriveSeed(int sessionSeed, string orgId) => sessionSeed ^ unchecked((int)Fnv1a32(orgId));

		public static Random Create(int sessionSeed, string orgId) => new Random(DeriveSeed(sessionSeed, orgId));

		static uint Fnv1a32(string value) {
			uint hash = 2166136261;
			foreach (char c in value) {
				hash ^= c;
				hash *= 16777619;
			}
			return hash;
		}
	}
}
