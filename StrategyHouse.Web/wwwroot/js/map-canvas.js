// Movement 2 — collaborative Map canvas with drag-and-drop + SignalR sync
(function () {
    const canvas = document.getElementById('canvas');
    if (!canvas) return;
    const mapId = parseInt(canvas.dataset.mapId, 10);
    const sessionCode = new URLSearchParams(location.search).get('code') || '';

    let draggingItem = null;

    document.querySelectorAll('.palette-item').forEach(el => {
        el.addEventListener('dragstart', (e) => {
            draggingItem = el;
            el.classList.add('dragging');
            const payload = {
                kind: el.dataset.kind,
                id: el.dataset.id ? parseInt(el.dataset.id, 10) : null,
                label: el.dataset.kind === 'custom' ? (document.getElementById('customLabel')?.value || 'عنصر') : el.textContent.trim()
            };
            e.dataTransfer.setData('application/json', JSON.stringify(payload));
            e.dataTransfer.effectAllowed = 'copy';
        });
        el.addEventListener('dragend', () => {
            el.classList.remove('dragging');
            draggingItem = null;
        });
    });

    document.querySelectorAll('.element-card').forEach(slot => {
        slot.addEventListener('dragover', (e) => { e.preventDefault(); slot.classList.add('drag-over'); });
        slot.addEventListener('dragleave', () => slot.classList.remove('drag-over'));
        slot.addEventListener('drop', async (e) => {
            e.preventDefault();
            slot.classList.remove('drag-over');
            const elementId = parseInt(slot.dataset.elementId, 10);
            let payload;
            try { payload = JSON.parse(e.dataTransfer.getData('application/json')); } catch { return; }

            const body = new URLSearchParams();
            body.set('mapId', mapId);
            body.set('elementId', elementId);
            body.set('kind', payload.kind);
            if (payload.kind === 'project') body.set('projectId', payload.id);
            if (payload.kind === 'kpi') body.set('kpiId', payload.id);
            if (payload.kind === 'role') body.set('roleId', payload.id);
            if (payload.kind === 'custom') body.set('customLabel', payload.label);

            const res = await fetch('/Session/AddPlacement', { method: 'POST', body });
            const json = await res.json();
            if (json.ok) {
                addPlacementToDom(slot, json.id, payload.label);
                if (hub) hub.invoke('BroadcastPlacement', sessionCode, { mapId, elementId, id: json.id, label: payload.label });
            }
        });
    });

    function addPlacementToDom(slot, placementId, label) {
        slot.classList.add('has-items');
        let items = slot.querySelector('.element-card__items');
        if (!items) { items = document.createElement('div'); items.className = 'element-card__items'; slot.appendChild(items); }
        const chip = document.createElement('span');
        chip.className = 'placed-item';
        chip.dataset.placementId = placementId;
        chip.innerHTML = `${escapeHtml(label)} <button type="button" onclick="removePlacement(${placementId}, this)">×</button>`;
        items.appendChild(chip);
    }

    function escapeHtml(s) { return s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

    window.removePlacement = async function (placementId, btn) {
        const body = new URLSearchParams();
        body.set('placementId', placementId);
        const res = await fetch('/Session/RemovePlacement', { method: 'POST', body });
        const json = await res.json();
        if (json.ok) btn.parentElement.remove();
    };

    // SignalR connection
    let hub = null;
    try {
        hub = new signalR.HubConnectionBuilder().withUrl('/hubs/canvas').withAutomaticReconnect().build();
        hub.on('PlacementAdded', (p) => {
            const slot = document.querySelector(`.element-card[data-element-id="${p.elementId}"]`);
            if (slot && !slot.querySelector(`[data-placement-id="${p.id}"]`)) addPlacementToDom(slot, p.id, p.label);
        });
        hub.start()
            .then(() => {
                document.getElementById('liveStatus')?.classList.remove('off');
                document.getElementById('liveStatus').textContent = 'متصل مباشرة';
                hub.invoke('JoinSession', sessionCode);
            })
            .catch(() => { /* sync disabled, still works locally */ });
    } catch (e) { /* sync optional */ }
})();
