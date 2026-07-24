// Flat single IndexedDB object store for all persisted files (path -> content),
// backing GS.Game.WebClient.Services.IndexedDbPersistence. Save data is small enough
// that a single store filtered by prefix at load time is simpler than one store per
// directory.
window.gameStorage = (function () {
	const dbName = "GameStorage";
	const storeName = "files";
	let dbPromise = null;

	function openDb() {
		if (dbPromise) {
			return dbPromise;
		}
		dbPromise = new Promise(function (resolve, reject) {
			const request = indexedDB.open(dbName, 1);
			request.onupgradeneeded = function (event) {
				const db = event.target.result;
				if (!db.objectStoreNames.contains(storeName)) {
					db.createObjectStore(storeName);
				}
			};
			request.onsuccess = function (event) {
				resolve(event.target.result);
			};
			request.onerror = function (event) {
				reject(event.target.error);
			};
		});
		return dbPromise;
	}

	async function loadAll(prefix) {
		const db = await openDb();
		return new Promise(function (resolve, reject) {
			const tx = db.transaction(storeName, "readonly");
			const store = tx.objectStore(storeName);
			const request = store.openCursor();
			const results = [];
			request.onsuccess = function (event) {
				const cursor = event.target.result;
				if (cursor) {
					const key = cursor.key;
					if (!prefix || key.startsWith(prefix)) {
						results.push({ key: key, value: cursor.value });
					}
					cursor.continue();
				} else {
					resolve(results);
				}
			};
			request.onerror = function (event) {
				reject(event.target.error);
			};
		});
	}

	async function put(key, value) {
		const db = await openDb();
		return new Promise(function (resolve, reject) {
			const tx = db.transaction(storeName, "readwrite");
			tx.objectStore(storeName).put(value, key);
			tx.oncomplete = function () { resolve(); };
			tx.onerror = function (event) { reject(event.target.error); };
		});
	}

	async function del(key) {
		const db = await openDb();
		return new Promise(function (resolve, reject) {
			const tx = db.transaction(storeName, "readwrite");
			tx.objectStore(storeName).delete(key);
			tx.oncomplete = function () { resolve(); };
			tx.onerror = function (event) { reject(event.target.error); };
		});
	}

	return {
		loadAll: loadAll,
		put: put,
		delete: del
	};
})();
