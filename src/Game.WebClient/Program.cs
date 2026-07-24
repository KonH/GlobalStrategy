using System;
using System.Net.Http;
using System.Threading.Tasks;
using GS.Game.WebClient.Services;
using GS.Game.WebClient.Terminal;
using GS.Game.WebClient.Terminal.Suggestions;
using GS.Main;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace GS.Game.WebClient {

	public static class Program {

		public static async Task Main(string[] args) {
			var builder = WebAssemblyHostBuilder.CreateDefault(args);
			builder.RootComponents.Add<App>("#app");

			// Blazor WASM runs the whole app in a single DI scope, so AddScoped here just
			// adds unnecessary scope-validation friction against the rest of this file's
			// singletons (ConfigProvider et al. depend on HttpClient directly) - AddSingleton
			// matches how every other service in this file is registered.
			builder.Services.AddSingleton(sp => new HttpClient {
				BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
			});
			builder.Services.AddSingleton<ConfigProvider>();
			builder.Services.AddSingleton<IGameConfigSource>(sp => sp.GetRequiredService<ConfigProvider>());
			builder.Services.AddSingleton<Localization>();
			builder.Services.AddSingleton<ILocalization>(sp => sp.GetRequiredService<Localization>());
			builder.Services.AddSingleton<IGameLogger, ConsoleGameLogger>();
			builder.Services.AddSingleton<IStoragePersistence, IndexedDbPersistence>();
			builder.Services.AddSingleton<BrowserStorage>();
			builder.Services.AddSingleton<IPersistentStorage>(sp => sp.GetRequiredService<BrowserStorage>());
			builder.Services.AddSingleton<ISnapshotSerializer, WebSnapshotSerializer>();
			// WebAssemblyHostBuilder only registers the JS interop runtime under IJSRuntime -
			// its concrete DefaultWebAssemblyJSRuntime also implements IJSInProcessRuntime, but
			// nothing exposes it under that interface unless we do it ourselves here.
			builder.Services.AddSingleton(sp => (IJSInProcessRuntime)sp.GetRequiredService<IJSRuntime>());
			builder.Services.AddSingleton<IPreferencesStore, LocalStoragePreferencesStore>();
			builder.Services.AddSingleton<AppPreferences>();
			builder.Services.AddSingleton<GameSession>();
			builder.Services.AddSingleton<CommandRegistry>();
			builder.Services.AddSingleton<CommandExecutor>();
			builder.Services.AddSingleton<SuggestionValueResolver>();
			builder.Services.AddSingleton<SuggestionEngine>();

			var host = builder.Build();

			var browserStorage = host.Services.GetRequiredService<BrowserStorage>();
			await browserStorage.InitializeAsync();

			var configProvider = host.Services.GetRequiredService<ConfigProvider>();
			await configProvider.InitializeAsync();

			var localization = host.Services.GetRequiredService<Localization>();
			await localization.InitializeAsync();

			await host.RunAsync();
		}
	}
}
