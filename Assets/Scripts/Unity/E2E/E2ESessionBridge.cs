using System;
using GS.Main;
using GS.Unity.UI;
using VContainer.Unity;

namespace GS.Unity.E2E {
	public class E2ESessionBridge : IStartable, IDisposable {
		public VisualState VisualState { get; }
		public IWriteOnlyCommandAccessor Commands { get; }
		public SaveFileManager Saves { get; }
		public ILocalization Localization { get; }

		public E2ESessionBridge(
			VisualState visualState,
			IWriteOnlyCommandAccessor commands,
			SaveFileManager saves,
			ILocalization localization
		) {
			VisualState = visualState;
			Commands = commands;
			Saves = saves;
			Localization = localization;
		}

		public void Start() {
			E2ERunnerHost.AttachBridge(this);
		}

		public void Dispose() {
			E2ERunnerHost.DetachBridge(this);
		}
	}
}
