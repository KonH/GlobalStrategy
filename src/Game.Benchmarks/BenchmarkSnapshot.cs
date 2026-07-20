using System;
using System.Collections.Generic;

namespace GS.Game.Benchmarks {
	public class BenchmarkEntry {
		public string Name { get; set; } = "";
		public double MeanNanoseconds { get; set; }
		public double StdDevNanoseconds { get; set; }
		public long AllocatedBytes { get; set; }
	}

	public class BenchmarkRunRecord {
		public DateTime Timestamp { get; set; }
		public string Mode { get; set; } = "";
		public List<BenchmarkEntry> Results { get; set; } = new();
		public Dictionary<string, bool> Verdicts { get; set; } = new();
		public bool Pass { get; set; }
	}
}
