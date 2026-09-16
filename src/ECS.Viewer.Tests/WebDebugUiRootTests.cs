using System;
using System.IO;
using System.Text;
using ECS;
using ECS.Viewer;
using ECS.Viewer.Host;
using Xunit;

namespace ECS.Viewer.Tests {
	public class WebDebugUiRootTests : IDisposable {
		readonly string _dir;

		public WebDebugUiRootTests() {
			_dir = Path.Combine(Path.GetTempPath(), "web-debug-ui-root-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_dir);
		}

		public void Dispose() {
			try {
				Directory.Delete(_dir, recursive: true);
			} catch {
			}
		}

		[Fact]
		public void FromPublishDirectory_UsesWwwrootWhenBlazorLayoutIsNested() {
			string wwwroot = Path.Combine(_dir, "wwwroot");
			Directory.CreateDirectory(Path.Combine(wwwroot, "_framework"));
			File.WriteAllText(Path.Combine(wwwroot, "index.html"), "<html>nested</html>");

			string resolved = WebDebugUiRoot.FromPublishDirectory(_dir);

			Assert.Equal(Path.GetFullPath(wwwroot), resolved);
		}

		[Fact]
		public void FromPublishDirectory_KeepsFlatLayout() {
			Directory.CreateDirectory(Path.Combine(_dir, "_framework"));
			File.WriteAllText(Path.Combine(_dir, "index.html"), "<html>flat</html>");

			string resolved = WebDebugUiRoot.FromPublishDirectory(_dir);

			Assert.Equal(Path.GetFullPath(_dir), resolved);
		}

		[Fact]
		public void Handle_GetRoot_ServesNestedWwwrootIndex() {
			string wwwroot = Path.Combine(_dir, "wwwroot");
			Directory.CreateDirectory(Path.Combine(wwwroot, "_framework"));
			File.WriteAllText(Path.Combine(wwwroot, "index.html"), "<html>nested-shell</html>");

			var world = new World();
			var handler = new ViewerRequestHandler(
				_dir,
				new SimulationMarshal(),
				new PauseToken(),
				new WorldObserver(),
				() => world);

			ViewerHttpResult result = handler.Handle("GET", "/", "", "");

			Assert.Equal(200, result.StatusCode);
			Assert.Equal("<html>nested-shell</html>", Encoding.UTF8.GetString(result.Body));
		}
	}
}
