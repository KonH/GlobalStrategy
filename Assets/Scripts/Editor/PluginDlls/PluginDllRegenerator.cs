using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GS.Editor.PluginDlls {
	/// <summary>
	/// Regenerates gitignored <c>Assets/Plugins/Core</c> DLLs from <c>src/</c>
	/// when they are missing or stale. Runs on Editor load and before player builds.
	/// Keep watch prefixes in sync with <c>scripts/unity/ensure_plugin_dlls.py</c>.
	/// </summary>
	[InitializeOnLoad]
	public sealed class PluginDllRegenerator : IPreprocessBuildWithReport {
		const string PluginsRelativeDir = "Assets/Plugins/Core";
		const string SolutionRelativePath = "src/GlobalStrategy.Core.sln";
		const string LogRelativePath = ".tmp/plugin-dll-regenerate.log";
		const string WebClientRelativeDir = "src/Game.WebClient";
		const string WebPublishRelativeDir = ".tmp/web-debug-ui";
		const string WebPublishLogRelativePath = ".tmp/web-debug-ui-publish.log";
		const int BuildTimeoutMs = 10 * 60 * 1000;

		static readonly string[] WatchPrefixes = {
			"src/Core.Configs",
			"src/Core.Map",
			"src/ECS.Core",
			"src/ECS.Core.Extensions",
			"src/ECS.Viewer",
			"src/ECS.Viewer.Host",
			"src/Game.Bots",
			"src/Game.Commands",
			"src/Game.Commands.Text",
			"src/Game.Common",
			"src/Game.Components",
			"src/Game.Configs",
			"src/Game.E2E",
			"src/Game.Main",
			"src/Game.Systems",
			"src/Directory.Build.props",
		};

		static Process _buildProcess;
		static bool _refreshQueued;

		public int callbackOrder => -1000;

		static PluginDllRegenerator() {
			EditorApplication.delayCall += TryRegenerateIfNeeded;
		}

		[MenuItem("GS/Plugins/Regenerate Core DLLs")]
		static void RegenerateFromMenu() {
			StartBuild("menu", force: true, blocking: false);
		}

		public void OnPreprocessBuild(BuildReport report) {
			if (!NeedsRebuild(out string reason)) {
				return;
			}

			if (!RunBuildBlocking(reason, refresh: false)) {
				throw new BuildFailedException(
					$"[PluginDlls] Cannot start a player build: plugin DLLs are missing or stale ({reason}) and regeneration failed. Run `dotnet build src/GlobalStrategy.Core.sln -c Release` or GS/Plugins/Regenerate Core DLLs."
				);
			}
		}

		static void TryRegenerateIfNeeded() {
			if (EditorApplication.isPlayingOrWillChangePlaymode) {
				return;
			}

			if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
				EditorApplication.delayCall += TryRegenerateIfNeeded;
				return;
			}

			if (_buildProcess != null) {
				return;
			}

			if (!NeedsRebuild(out string reason)) {
				EnsureWebPublish();
				return;
			}

			StartBuild(reason, force: false, blocking: Application.isBatchMode);
		}

		static void StartBuild(string reason, bool force, bool blocking) {
			if (!force && !NeedsRebuild(out reason)) {
				return;
			}

			if (blocking) {
				if (!RunBuildBlocking(reason, refresh: true)) {
					Debug.LogError($"[PluginDlls] Regeneration failed ({reason}). See {LogRelativePath}.");
				}

				return;
			}

			if (!TryStartBuildProcess(reason, out string error)) {
				Debug.LogError($"[PluginDlls] {error}");
				return;
			}

			EditorApplication.update += PollBuildProcess;
		}

		static bool RunBuildBlocking(string reason, bool refresh) {
			if (!TryStartBuildProcess(reason, out string error)) {
				Debug.LogError($"[PluginDlls] {error}");
				return false;
			}

			try {
				if (!_buildProcess.WaitForExit(BuildTimeoutMs)) {
					try {
						_buildProcess.Kill();
					} catch (InvalidOperationException) {
					}

					EditorApplication.UnlockReloadAssemblies();
					Debug.LogError($"[PluginDlls] Regeneration timed out after {BuildTimeoutMs} ms.");
					return false;
				}

				return FinishBuild(_buildProcess.ExitCode, refresh);
			} finally {
				CleanupBuildProcess();
			}
		}

		static bool TryStartBuildProcess(string reason, out string error) {
			error = "";
			if (_buildProcess != null) {
				error = "a plugin DLL build is already running.";
				return false;
			}

			string projectRoot = ProjectRoot();
			string dotnet = ResolveDotnet();
			if (dotnet == null) {
				error = "dotnet SDK not found. Install .NET 8 and ensure `dotnet` is on PATH, or set DOTNET_ROOT.";
				return false;
			}

			string logPath = Path.Combine(projectRoot, LogRelativePath.Replace('/', Path.DirectorySeparatorChar));
			string logDir = Path.GetDirectoryName(logPath);
			if (!string.IsNullOrEmpty(logDir)) {
				Directory.CreateDirectory(logDir);
			}

			var startInfo = new ProcessStartInfo {
				FileName = dotnet,
				Arguments = $"build \"{SolutionRelativePath}\" -c Release -flp:LogFile=\"{logPath}\";Verbosity=minimal",
				WorkingDirectory = projectRoot,
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			Debug.Log($"[PluginDlls] Regenerating Core DLLs ({reason}) with {dotnet} {startInfo.Arguments}");
			EditorApplication.LockReloadAssemblies();
			try {
				_buildProcess = Process.Start(startInfo);
				if (_buildProcess == null) {
					error = "failed to start `dotnet build`.";
					EditorApplication.UnlockReloadAssemblies();
					return false;
				}
			} catch (Exception ex) {
				EditorApplication.UnlockReloadAssemblies();
				_buildProcess = null;
				error = $"failed to start `dotnet build`: {ex.Message}";
				return false;
			}

			return true;
		}

		static void PollBuildProcess() {
			if (_buildProcess == null) {
				EditorApplication.update -= PollBuildProcess;
				return;
			}

			if (!_buildProcess.HasExited) {
				return;
			}

			EditorApplication.update -= PollBuildProcess;
			int exitCode = _buildProcess.ExitCode;
			CleanupBuildProcess();
			FinishBuild(exitCode, refresh: true);
		}

		static bool FinishBuild(int exitCode, bool refresh) {
			EditorApplication.UnlockReloadAssemblies();
			if (exitCode != 0) {
				Debug.LogError($"[PluginDlls] `dotnet build -c Release` failed (exit {exitCode}). See {LogRelativePath}.");
				return false;
			}

			if (NeedsRebuild(out string reason)) {
				Debug.LogError($"[PluginDlls] Build finished but plugin DLLs still need regeneration ({reason}).");
				return false;
			}

			Debug.Log("[PluginDlls] Core DLLs regenerated.");
			EnsureWebPublish();
			if (refresh && !_refreshQueued) {
				_refreshQueued = true;
				AssetDatabase.Refresh();
			}

			return true;
		}

		static void CleanupBuildProcess() {
			if (_buildProcess == null) {
				return;
			}

			_buildProcess.Dispose();
			_buildProcess = null;
		}

		static bool NeedsRebuild(out string reason) {
			string pluginsDir = PluginsDir();
			List<string> expected = ExpectedDlls(pluginsDir);
			if (expected.Count == 0) {
				reason = $"no *.dll.meta files found under {PluginsRelativeDir}";
				return true;
			}

			var missing = new List<string>();
			foreach (string dll in expected) {
				if (!File.Exists(dll)) {
					missing.Add(Path.GetFileName(dll));
				}
			}

			if (missing.Count > 0) {
				reason = "missing plugin DLL(s): " + string.Join(", ", missing);
				return true;
			}

			DateTime? srcMtime = NewestWatchMtime();
			if (srcMtime == null) {
				reason = "no watched src/ files";
				return false;
			}

			DateTime oldestDll = DateTime.MaxValue;
			foreach (string dll in expected) {
				DateTime writeTime = File.GetLastWriteTimeUtc(dll);
				if (writeTime < oldestDll) {
					oldestDll = writeTime;
				}
			}

			if (srcMtime.Value > oldestDll) {
				reason = "src/ is newer than Assets/Plugins/Core DLLs";
				return true;
			}

			reason = "plugin DLLs are up to date";
			return false;
		}

		static List<string> ExpectedDlls(string pluginsDir) {
			var dlls = new List<string>();
			if (!Directory.Exists(pluginsDir)) {
				return dlls;
			}

			foreach (string meta in Directory.GetFiles(pluginsDir, "*.dll.meta")) {
				dlls.Add(meta.Substring(0, meta.Length - ".meta".Length));
			}

			dlls.Sort(StringComparer.OrdinalIgnoreCase);
			return dlls;
		}

		static DateTime? NewestWatchMtime() {
			string projectRoot = ProjectRoot();
			DateTime? newest = null;
			foreach (string prefix in WatchPrefixes) {
				string target = Path.Combine(projectRoot, prefix);
				if (File.Exists(target)) {
					Consider(File.GetLastWriteTimeUtc(target), ref newest);
					continue;
				}

				if (!Directory.Exists(target)) {
					continue;
				}

				foreach (string path in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)) {
					if (IsSkippedPath(path, target)) {
						continue;
					}

					string ext = Path.GetExtension(path);
					string name = Path.GetFileName(path);
					if (!ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
						&& !ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
						&& !name.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase)) {
						continue;
					}

					Consider(File.GetLastWriteTimeUtc(path), ref newest);
				}
			}

			return newest;
		}

		static bool IsSkippedPath(string path, string root) {
			string relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			foreach (string part in parts) {
				if (part.Equals("bin", StringComparison.OrdinalIgnoreCase)
					|| part.Equals("obj", StringComparison.OrdinalIgnoreCase)) {
					return true;
				}
			}

			return false;
		}

		static void Consider(DateTime writeTime, ref DateTime? newest) {
			if (newest == null || writeTime > newest.Value) {
				newest = writeTime;
			}
		}

		static string ResolveDotnet() {
			string root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
			if (!string.IsNullOrEmpty(root)) {
				string exe = Path.Combine(root, "dotnet.exe");
				if (File.Exists(exe)) {
					return exe;
				}

				string unix = Path.Combine(root, "dotnet");
				if (File.Exists(unix)) {
					return unix;
				}
			}

			string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			if (!string.IsNullOrEmpty(programFiles)) {
				string pfDotnet = Path.Combine(programFiles, "dotnet", "dotnet.exe");
				if (File.Exists(pfDotnet)) {
					return pfDotnet;
				}
			}

			return CanRunDotnetOnPath() ? "dotnet" : null;
		}

		static bool CanRunDotnetOnPath() {
			try {
				var startInfo = new ProcessStartInfo {
					FileName = "dotnet",
					Arguments = "--version",
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true,
				};
				using var process = Process.Start(startInfo);
				if (process == null) {
					return false;
				}

				return process.WaitForExit(5000) && process.ExitCode == 0;
			} catch (Exception) {
				return false;
			}
		}

		static string ProjectRoot() {
			return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
		}

		static string PluginsDir() {
			return Path.Combine(ProjectRoot(), PluginsRelativeDir.Replace('/', Path.DirectorySeparatorChar));
		}

		static string PublishedWebIndex(string publishDir) {
			string nested = Path.Combine(publishDir, "wwwroot", "index.html");
			if (File.Exists(nested)) {
				return nested;
			}
			return Path.Combine(publishDir, "index.html");
		}

		static string PublishedWebFramework(string publishDir) {
			string nested = Path.Combine(publishDir, "wwwroot", "_framework");
			if (Directory.Exists(nested)) {
				return nested;
			}
			return Path.Combine(publishDir, "_framework");
		}

		static bool NeedsWebPublish(out string reason) {
			string publishDir = Path.Combine(ProjectRoot(), WebPublishRelativeDir.Replace('/', Path.DirectorySeparatorChar));
			string index = PublishedWebIndex(publishDir);
			string framework = PublishedWebFramework(publishDir);
			if (!File.Exists(index) || !Directory.Exists(framework)) {
				reason = "published Blazor UI is missing";
				return true;
			}

			DateTime? srcMtime = NewestWebClientMtime();
			if (srcMtime == null) {
				reason = "no Game.WebClient sources";
				return false;
			}

			if (srcMtime.Value > File.GetLastWriteTimeUtc(index)) {
				reason = "src/Game.WebClient is newer than .tmp/web-debug-ui";
				return true;
			}

			reason = "published Blazor UI is up to date";
			return false;
		}

		static DateTime? NewestWebClientMtime() {
			string target = Path.Combine(ProjectRoot(), WebClientRelativeDir.Replace('/', Path.DirectorySeparatorChar));
			if (!Directory.Exists(target)) {
				return null;
			}

			string wwwroot = Path.Combine(target, "wwwroot");
			DateTime? newest = null;
			foreach (string path in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)) {
				if (IsSkippedPath(path, target)) {
					continue;
				}

				string ext = Path.GetExtension(path);
				bool inWwwroot = path.StartsWith(wwwroot, StringComparison.OrdinalIgnoreCase);
				if (!ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
					&& !ext.Equals(".razor", StringComparison.OrdinalIgnoreCase)
					&& !ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
					&& !inWwwroot) {
					continue;
				}

				Consider(File.GetLastWriteTimeUtc(path), ref newest);
			}

			return newest;
		}

		static void EnsureWebPublish() {
			if (!NeedsWebPublish(out string reason)) {
				return;
			}

			string projectRoot = ProjectRoot();
			string dotnet = ResolveDotnet();
			if (dotnet == null) {
				Debug.LogError("[PluginDlls] Cannot publish Game.WebClient: dotnet SDK not found.");
				return;
			}

			string logPath = Path.Combine(projectRoot, WebPublishLogRelativePath.Replace('/', Path.DirectorySeparatorChar));
			string logDir = Path.GetDirectoryName(logPath);
			if (!string.IsNullOrEmpty(logDir)) {
				Directory.CreateDirectory(logDir);
			}

			string outDir = Path.Combine(projectRoot, WebPublishRelativeDir.Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(outDir);

			var startInfo = new ProcessStartInfo {
				FileName = dotnet,
				Arguments = $"publish \"{WebClientRelativeDir}\" -c Release -o \"{outDir}\" -flp:LogFile=\"{logPath}\";Verbosity=minimal",
				WorkingDirectory = projectRoot,
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			Debug.Log($"[PluginDlls] Publishing Game.WebClient ({reason})");
			try {
				using var process = Process.Start(startInfo);
				if (process == null) {
					Debug.LogError("[PluginDlls] failed to start `dotnet publish` for Game.WebClient.");
					return;
				}

				if (!process.WaitForExit(BuildTimeoutMs) || process.ExitCode != 0) {
					Debug.LogError($"[PluginDlls] `dotnet publish src/Game.WebClient` failed. See {WebPublishLogRelativePath}.");
				}
			} catch (Exception ex) {
				Debug.LogError($"[PluginDlls] `dotnet publish` failed: {ex.Message}");
			}
		}
	}
}
