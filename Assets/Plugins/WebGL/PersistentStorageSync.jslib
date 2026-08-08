// Explicitly flushes Application.persistentDataPath writes (Emscripten MEMFS) to IndexedDB.
//
// `autoSyncPersistentDataPath` (set in Assets/WebGLTemplates/Minimal/index.html) already
// schedules a sync after writes, but that has proven unreliable in practice — see
// .claude/rules/unity/webgl.md. Calling this right after every PersistentStorage write/delete
// starts the flush immediately instead of waiting on the auto path's internal timing, and
// surfaces a failure (e.g. storage blocked/partitioned in a third-party iframe embed, such as
// the Unity Play listing) as a console error instead of a silently dropped save.
mergeInto(LibraryManager.library, {
	PersistentStorageSyncFs: function () {
		if (typeof FS === "undefined" || typeof FS.syncfs !== "function") {
			console.error("[PersistentStorage] FS.syncfs unavailable; cannot flush persistentDataPath to IndexedDB.");
			return;
		}
		console.log("[PersistentStorage] FS.syncfs starting...");
		FS.syncfs(false, function (err) {
			if (err) {
				console.error("[PersistentStorage] FS.syncfs failed to flush persistentDataPath to IndexedDB: " + err);
			} else {
				console.log("[PersistentStorage] FS.syncfs succeeded.");
			}
		});
	}
});
