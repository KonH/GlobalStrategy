using System;
using System.Net.Http;
using System.Threading.Tasks;
using GS.Game.WebClient.Services;
using GS.Game.WebClient.Terminal.Suggestions;
using GS.Game.Commands.Text;
using GS.Game.Commands.Text.Suggestions;
using GS.Main;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace GS.Game.WebClient {

	public static class Program {

		public static async Task Main(string[] args) {
			var builder = WebAssemblyHostBuilder.CreateDefault(args);
			builder.RootComponents.Add<App>("#app");

			builder.Services.AddSingleton(sp => new HttpClient {
				BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
			});
			builder.Services.AddSingleton<ConfigProvider>();
			builder.Services.AddSingleton<IGameConfigSource>(sp => sp.GetRequiredService<ConfigProvider>());
			builder.Services.AddSingleton<Localization>();
			builder.Services.AddSingleton<ILocalization>(sp => sp.GetRequiredService<Localization>());
			builder.Services.AddSingleton<ISuggestionLabels, LocalizationSuggestionLabels>();
			builder.Services.AddSingleton<IGameLogger, ConsoleGameLogger>();
			builder.Services.AddSingleton<IStoragePersistence, IndexedDbPersistence>();
			builder.Services.AddSingleton<BrowserStorage>();
			builder.Services.AddSingleton<IPersistentStorage>(sp => sp.GetRequiredService<BrowserStorage>());
			builder.Services.AddSingleton<ISnapshotSerializer, WebSnapshotSerializer>();
			builder.Services.AddSingleton(sp => (IJSInProcessRuntime)sp.GetRequiredService<IJSRuntime>());
			builder.Services.AddSingleton<IPreferencesStore, LocalStoragePreferencesStore>();
			builder.Services.AddSingleton<AppPreferences>();
			builder.Services.AddSingleton<GameSession>();
			builder.Services.AddSingleton<CommandRegistry>();
			builder.Services.AddSingleton<CommandExecutor>();
			builder.Services.AddSingleton<ISuggestionSource>(sp => {
				var session = sp.GetRequiredService<GameSession>();
				var configs = sp.GetRequiredService<IGameConfigSource>();
				ISuggestionSource fallback = WebConfigSuggestionCatalog.From(configs);
				return new DelegatingSuggestionSource(() => session.Logic != null
					? new GameLogicSuggestionSource(session.Logic)
					: fallback);
			});
			builder.Services.AddSingleton<SuggestionValueResolver>();
			builder.Services.AddSingleton<SuggestionEngine>();
			builder.Services.AddSingleton<IGameClient>(sp => {
				var nav = sp.GetRequiredService<NavigationManager>();
				if (HostMode.IsRemote(nav.Uri)) {
					return new RemoteGameClient(sp.GetRequiredService<HttpClient>());
				}
				return new InProcessGameClient(
					sp.GetRequiredService<GameSession>(),
					sp.GetRequiredService<CommandExecutor>(),
					sp.GetRequiredService<SuggestionEngine>(),
					sp.GetRequiredService<ISuggestionSource>());
			});

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
