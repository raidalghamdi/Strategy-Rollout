// Lightweight signature pad — touch + mouse, exports as base64 PNG
(function () {
    const canvas = document.getElementById('sigPad');
    if (!canvas) return;
    const ctx = canvas.getContext('2d');

    function resize() {
        const rect = canvas.getBoundingClientRect();
        canvas.width = rect.width * window.devicePixelRatio;
        canvas.height = rect.height * window.devicePixelRatio;
        ctx.scale(window.devicePixelRatio, window.devicePixelRatio);
        ctx.lineWidth = 2.5;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.strokeStyle = '#1B5E7F';
    }
    resize();

    let drawing = false;
    let last = null;

    function pos(e) {
        const r = canvas.getBoundingClientRect();
        const t = e.touches ? e.touches[0] : e;
        return { x: t.clientX - r.left, y: t.clientY - r.top };
    }
    function start(e) { drawing = true; last = pos(e); e.preventDefault(); }
    function move(e) {
        if (!drawing) return;
        const p = pos(e);
        ctx.beginPath(); ctx.moveTo(last.x, last.y); ctx.lineTo(p.x, p.y); ctx.stroke();
        last = p;
        e.preventDefault();
    }
    function end() { drawing = false; }

    canvas.addEventListener('mousedown', start);
    canvas.addEventListener('mousemove', move);
    canvas.addEventListener('mouseup', end);
    canvas.addEventListener('mouseleave', end);
    canvas.addEventListener('touchstart', start, { passive: false });
    canvas.addEventListener('touchmove', move, { passive: false });
    canvas.addEventListener('touchend', end);

    window.clearSig = function () {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
    };

    window.saveSig = async function () {
        const name = document.getElementById('signerName').value.trim();
        if (!name) { alert('اكتب اسمك أولاً'); return; }
        // Quick "is canvas empty" heuristic: check if any pixel is non-transparent
        const data = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
        let hasInk = false;
        for (let i = 3; i < data.length; i += 4) { if (data[i] !== 0) { hasInk = true; break; } }
        if (!hasInk) { alert('وقّع داخل المربع أولاً'); return; }

        const png = canvas.toDataURL('image/png');
        const body = new URLSearchParams();
        body.set('mapId', document.getElementById('mapId').value);
        body.set('signerName', name);
        body.set('signaturePng', png);
        const res = await fetch('/Session/Sign', { method: 'POST', body });
        if ((await res.json()).ok) {
            const chip = document.createElement('span');
            chip.className = 'signature-chip';
            chip.textContent = '✓ ' + name;
            document.getElementById('sigList').appendChild(chip);
            document.getElementById('signerName').value = '';
            window.clearSig();
        }
    };
})();
