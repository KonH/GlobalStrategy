using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GS.Unity.E2E {
	public class E2EConsoleCollector {
		readonly List<string> _buffer = new List<string>();
		int _errorCount;
		readonly List<string> _errorQuotes = new List<string>();

		public int ErrorCount => _errorCount;
		public IReadOnlyList<string> ErrorQuotes => _errorQuotes;

		public void Start() {
			Application.logMessageReceived += OnLog;
		}

		public void Stop() {
			Application.logMessageReceived -= OnLog;
		}

		public string FlushStep() {
			var text = new StringBuilder();
			foreach (var line in _buffer) {
				text.AppendLine(line);
			}
			_buffer.Clear();
			return text.ToString();
		}

		void OnLog(string condition, string stackTrace, LogType type) {
			_buffer.Add($"[{type}] {condition}");
			if (!string.IsNullOrEmpty(stackTrace)) {
				_buffer.Add(stackTrace);
			}
			if (type == LogType.Error || type == LogType.Exception) {
				_errorCount++;
				if (_errorQuotes.Count < 8) {
					_errorQuotes.Add(condition);
				}
			}
		}
	}
}
