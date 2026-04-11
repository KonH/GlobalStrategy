/* ECS Viewer — app.js */
'use strict';

let snapshot = null;
let paused = false;
let selectedEntityId = null;
// { typeName: 'require' | 'exclude' | null }
const filters = {};
let inputFocused = false;

const pauseBtn = document.getElementById('pauseBtn');
const entityList = document.getElementById('entityList');
const detail = document.getElementById('detail');
const filterRow = document.getElementById('filterRow');
const status = document.getElementById('status');
const entityCount = document.getElementById('entityCount');

pauseBtn.addEventListener('click', async () => {
  paused = !paused;
  await fetch('/pause', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ paused })
  });
  updatePauseBtn();
  if (!paused) refresh();
});

function updatePauseBtn() {
  pauseBtn.textContent = paused ? 'Resume' : 'Pause';
  pauseBtn.classList.toggle('paused', paused);
}

// ── Fetch & render ──────────────────────────────────────────────────────────

async function refresh() {
  try {
    const resp = await fetch('/snapshot');
    snapshot = await resp.json();
    status.textContent = `updated ${new Date().toLocaleTimeString()}`;
    rebuildFilters();
    renderList();
    if (selectedEntityId !== null) {
      const ent = snapshot.Entities.find(e => e.Id === selectedEntityId);
      if (ent) renderDetail(ent); else clearDetail();
    }
    entityCount.textContent = `${snapshot.Entities.length} entities`;
  } catch (e) {
    status.textContent = 'error: ' + e.message;
  }
}

// ── Filter bar ──────────────────────────────────────────────────────────────

function allTypeNames() {
  if (!snapshot) return [];
  const set = new Set();
  for (const e of snapshot.Entities) {
    for (const c of e.Components) set.add(c.TypeName);
  }
  return [...set].sort();
}

function rebuildFilters() {
  const names = allTypeNames();
  // add new types
  for (const n of names) {
    if (!(n in filters)) filters[n] = null;
  }
  filterRow.innerHTML = '<label>Filter:</label>';
  for (const name of names) {
    const chip = document.createElement('div');
    chip.className = 'filter-chip';
    const span = document.createElement('span');
    span.textContent = name;
    const req = document.createElement('button');
    req.className = 'filter-btn require' + (filters[name] === 'require' ? ' active' : '');
    req.textContent = '+';
    req.title = 'Require';
    req.addEventListener('click', () => {
      filters[name] = filters[name] === 'require' ? null : 'require';
      rebuildFilters(); renderList();
    });
    const exc = document.createElement('button');
    exc.className = 'filter-btn exclude' + (filters[name] === 'exclude' ? ' active' : '');
    exc.textContent = '−';
    exc.title = 'Exclude';
    exc.addEventListener('click', () => {
      filters[name] = filters[name] === 'exclude' ? null : 'exclude';
      rebuildFilters(); renderList();
    });
    chip.append(req, span, exc);
    filterRow.appendChild(chip);
  }
}

function passesFilter(entity) {
  const compNames = new Set(entity.Components.map(c => c.TypeName));
  for (const [name, mode] of Object.entries(filters)) {
    if (mode === 'require' && !compNames.has(name)) return false;
    if (mode === 'exclude' && compNames.has(name)) return false;
  }
  return true;
}

// ── Entity list ─────────────────────────────────────────────────────────────

function renderList() {
  if (!snapshot) return;
  const visible = snapshot.Entities.filter(passesFilter);
  entityList.innerHTML = '';
  for (const ent of visible) {
    const row = document.createElement('div');
    row.className = 'entity-row' + (ent.Id === selectedEntityId ? ' selected' : '');
    row.dataset.id = ent.Id;
    const idSpan = document.createElement('span');
    idSpan.className = 'entity-id';
    idSpan.textContent = `#${ent.Id} `;
    const tags = document.createElement('span');
    tags.className = 'comp-tags';
    tags.textContent = ent.Components.map(c => c.TypeName).join(', ');
    row.append(idSpan, tags);
    row.addEventListener('click', () => selectEntity(ent.Id));
    entityList.appendChild(row);
  }
}

function selectEntity(id) {
  selectedEntityId = id;
  renderList();
  if (!snapshot) return;
  const ent = snapshot.Entities.find(e => e.Id === id);
  if (ent) renderDetail(ent); else clearDetail();
}

function clearDetail() {
  detail.innerHTML = '<p style="color:#555">Entity no longer alive</p>';
}

// ── Detail panel ─────────────────────────────────────────────────────────────

function renderDetail(entity) {
  detail.innerHTML = '';
  const h2 = document.createElement('h2');
  h2.textContent = `Entity #${entity.Id}`;
  detail.appendChild(h2);

  for (const comp of entity.Components) {
    const block = document.createElement('div');
    block.className = 'comp-block';
    const nameDiv = document.createElement('div');
    nameDiv.className = 'comp-name';
    nameDiv.textContent = comp.TypeName;
    block.appendChild(nameDiv);

    for (const [fname, fval] of Object.entries(comp.Fields)) {
      const row = document.createElement('div');
      row.className = 'field-row';
      const nameEl = document.createElement('span');
      nameEl.className = 'field-name';
      nameEl.textContent = fname;
      const valEl = document.createElement('span');
      valEl.className = 'field-value';

      if (fval !== null && typeof fval === 'object' && '__entityRef' in fval) {
        // EntityRef — render as link
        const link = document.createElement('span');
        link.className = 'entity-link';
        link.textContent = `→ #${fval.__entityRef}`;
        link.addEventListener('click', () => selectEntity(fval.__entityRef));
        valEl.appendChild(link);
      } else if (isEditable(fval)) {
        const input = document.createElement('input');
        input.value = String(fval ?? '');
        input.addEventListener('focus', () => { inputFocused = true; });
        input.addEventListener('blur', () => {
          inputFocused = false;
          const newVal = input.value;
          patchField(entity.Id, comp.TypeName, fname, newVal).then(ok => {
            if (ok) refresh();
          });
        });
        input.addEventListener('keydown', e => {
          if (e.key === 'Enter') input.blur();
          if (e.key === 'Escape') { input.value = String(fval ?? ''); input.blur(); }
        });
        valEl.appendChild(input);
      } else {
        valEl.textContent = fval === null ? 'null' : String(fval);
      }
      row.append(nameEl, valEl);
      block.appendChild(row);
    }
    detail.appendChild(block);
  }
}

function isEditable(val) {
  if (val === null) return false;
  const t = typeof val;
  return t === 'number' || t === 'boolean' || t === 'string';
}

async function patchField(entityId, typeName, fieldName, value) {
  try {
    const resp = await fetch(`/entity/${entityId}/component/${encodeURIComponent(typeName)}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ [fieldName]: value })
    });
    return resp.ok;
  } catch {
    return false;
  }
}

// ── Auto-refresh ─────────────────────────────────────────────────────────────

setInterval(async () => {
  if (paused || inputFocused) return;
  // Sync pause state from server on startup
  if (snapshot === null) {
    try {
      const r = await fetch('/pause');
      const d = await r.json();
      paused = d.paused;
      updatePauseBtn();
    } catch { }
  }
  refresh();
}, 500);

refresh();
