// Phase 20.1 — interactive strategy flow (Sankey) for stage 2 "تدفق تفاعلي" tab.
// Uses ECharts (loaded lazily from CDN). iOS Safari-safe: ES5 only, IIFE, string
// concat, for-loops, no template literals, no block scope, try/catch + fallback.
// Public API: window.StrategySankey.render(containerEl).
(function () {
    // Phase 20.8.4 — self-hosted ECharts because CSP blocks CDN scripts.
    var ECHARTS_SRC = '/lib/echarts/echarts.min.js';
    var loading = false;
    var queue = [];

    function loadEcharts(cb) {
        if (window.echarts) { cb(); return; }
        queue.push(cb);
        if (loading) return;
        loading = true;
        var s = document.createElement('script');
        s.src = ECHARTS_SRC;
        s.async = true;
        s.onload = function () {
            for (var i = 0; i < queue.length; i++) { try { queue[i](); } catch (e) {} }
            queue = [];
        };
        s.onerror = function () {
            loading = false;
            for (var i = 0; i < queue.length; i++) { try { queue[i](true); } catch (e) {} }
            queue = [];
        };
        document.head.appendChild(s);
    }

    function colorFor(category) {
        switch (category) {
            case 'vision': return '#00192B';
            case 'pillar': return '#067647';
            case 'objective': return '#299ECE';
            case 'initiative': return '#FAC126';
            case 'project': return '#28334A';
            default: return '#5a6b80';
        }
    }

    // Phase 20.1 — wrap long Arabic labels onto up to N lines so dense columns
    // (objectives/initiatives/projects) stay readable instead of clipping.
    function wrapN(name, maxCharsPerLine, maxLines) {
        var s = String(name || '').replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, '');
        if (!s) return '';
        var lines = [];
        var remaining = s;
        while (remaining.length > maxCharsPerLine && lines.length < maxLines - 1) {
            var cut = remaining.lastIndexOf(' ', maxCharsPerLine);
            if (cut <= 0) cut = maxCharsPerLine;
            lines.push(remaining.substring(0, cut));
            remaining = remaining.substring(cut).replace(/^\s+/, '');
        }
        if (remaining.length > maxCharsPerLine) {
            remaining = remaining.substring(0, maxCharsPerLine - 1) + '…';
        }
        lines.push(remaining);
        return lines.join('\n');
    }

    // Phase 20.1 — count nodes per column (layer) by walking the links graph.
    // The tallest column governs the required canvas height; using nodes.length
    // alone over-estimates short columns and under-estimates the busy ones.
    function tallestColumn(data) {
        var depth = {};
        var hasIncoming = {};
        for (var j = 0; j < data.links.length; j++) {
            hasIncoming[data.links[j].target] = true;
        }
        var bfs = [];
        for (var k = 0; k < data.nodes.length; k++) {
            var nm = data.nodes[k].name;
            if (!hasIncoming[nm]) { depth[nm] = 0; bfs.push(nm); }
        }
        var head = 0;
        while (head < bfs.length) {
            var cur = bfs[head++];
            var d = depth[cur];
            for (var l = 0; l < data.links.length; l++) {
                if (data.links[l].source === cur) {
                    var tgt = data.links[l].target;
                    var nd = d + 1;
                    if (depth[tgt] === undefined || depth[tgt] < nd) {
                        depth[tgt] = nd;
                        bfs.push(tgt);
                    }
                }
            }
        }
        var counts = {};
        var maxCount = 0;
        for (var name in depth) {
            if (!depth.hasOwnProperty(name)) continue;
            var dv = depth[name];
            counts[dv] = (counts[dv] || 0) + 1;
            if (counts[dv] > maxCount) maxCount = counts[dv];
        }
        return Math.max(maxCount, 1);
    }

    // Phase 20.9 — filter the dataset to only "strategic" or "operational" projects.
    // The chain Vision → Pillar → Objective → Initiative is preserved; only project
    // nodes (and links targeting them) are pruned.
    function filterData(data, kind) {
        if (!kind || kind === 'all') return data;
        var keepProject = {};
        for (var i = 0; i < data.nodes.length; i++) {
            var n = data.nodes[i];
            if (n.category !== 'project') continue;
            var k = (n.kind || 'other');
            // Unknown ("other") projects default to Strategic so we never blank the
            // chart when MSSQL hasn't classified rows yet.
            if (k === kind || (kind === 'strategic' && k === 'other')) {
                keepProject[n.name] = true;
            }
        }
        var filteredProjectNames = {};
        var nodes = [];
        for (var j = 0; j < data.nodes.length; j++) {
            var nn = data.nodes[j];
            if (nn.category === 'project' && !keepProject[nn.name]) {
                filteredProjectNames[nn.name] = true;
                continue;
            }
            nodes.push(nn);
        }
        var links = [];
        for (var l = 0; l < data.links.length; l++) {
            var lk = data.links[l];
            if (filteredProjectNames[lk.target]) continue;
            links.push(lk);
        }
        return { nodes: nodes, links: links, empty: data.empty, warning: data.warning };
    }

    function draw(el, data) {
        // Phase 20.8.6 — the previous formula (tallest × 72px) produced a ~6000px
        // canvas for a dense column of ~80 projects, which pushed all real nodes
        // far below the visible viewport: the user saw a tall white canvas and
        // could only reveal a tooltip near the bottom by accident. Cap height to
        // a realistic value that fits an iPad/desktop viewport while still giving
        // each node enough vertical room.
        var tallest = tallestColumn(data);
        var perNode = 36;
        var minHeight = 800;
        var maxHeight = 2400;
        var height = Math.min(maxHeight, Math.max(minHeight, tallest * perNode + 120));
        el.style.height = height + 'px';
        // Phase 20.9 — give the canvas a minimum width so dense graphs become
        // horizontally scrollable inside the parent .sankey-scroller wrapper
        // instead of squeezing labels off-screen.
        var minCanvasWidth = 1100;
        if (tallest > 30) minCanvasWidth = 1500;
        if (tallest > 60) minCanvasWidth = 1900;
        var parentW = (el.parentElement && el.parentElement.clientWidth) || 0;
        el.style.width = (parentW > minCanvasWidth ? '100%' : (minCanvasWidth + 'px'));

        var chart = window.echarts.init(el, null, { renderer: 'canvas' });
        var nodes = [];
        for (var i = 0; i < data.nodes.length; i++) {
            var n = data.nodes[i];
            nodes.push({
                name: n.name,
                itemStyle: { color: colorFor(n.category), borderWidth: 0 }
            });
        }
        var option = {
            tooltip: {
                trigger: 'item',
                triggerOn: 'mousemove',
                textStyle: { fontFamily: 'Cairo, sans-serif', fontSize: 13 },
                extraCssText: 'max-width:320px; white-space:normal; line-height:1.5;',
                formatter: function (p) {
                    if (p.dataType === 'node') {
                        return '<b style="font-family:Cairo;">' + p.name + '</b>';
                    }
                    if (p.dataType === 'edge' && p.data) {
                        return '<div style="font-family:Cairo;">' +
                               '<b>' + (p.data.source || '') + '</b>' +
                               ' ← ' +
                               '<b>' + (p.data.target || '') + '</b>' +
                               '</div>';
                    }
                    return p.name || '';
                }
            },
            series: [{
                type: 'sankey',
                // Phase 20.9 — give the rightmost column extra room so project names
                // render in full instead of being clipped by the canvas edge.
                left: 12, right: 200, top: 28, bottom: 28,
                data: nodes,
                links: data.links,
                emphasis: { focus: 'adjacency', lineStyle: { opacity: 0.7 } },
                lineStyle: { color: 'gradient', curveness: 0.5, opacity: 0.4 },
                label: {
                    show: true,
                    fontFamily: 'Cairo, sans-serif',
                    fontSize: 13,
                    fontWeight: 600,
                    color: '#1a2638',
                    formatter: function (p) { return wrapN(p.name, 28, 4); },
                    overflow: 'break',
                    width: 210,
                    lineHeight: 18
                },
                labelLayout: { hideOverlap: false },
                nodeWidth: 14,
                nodeGap: 16,
                nodeAlign: 'justify',
                draggable: false
            }]
        };
        chart.setOption(option);
        // Phase 20.8.5 — robust resize: ECharts measures the container at init();
        // if the tab/section was hidden the canvas can come out 0 × 0 and produce
        // a thin vertical bar. We retry multiple times AND use ResizeObserver so
        // the chart always fills the visible width once layout settles.
        function safeResize() { try { chart.resize(); } catch (e) {} }
        try { setTimeout(safeResize, 60); } catch (e1) {}
        try { setTimeout(safeResize, 250); } catch (e2) {}
        try { setTimeout(safeResize, 800); } catch (e3) {}
        if (!el.__resizeBound) {
            el.__resizeBound = true;
            window.addEventListener('resize', safeResize);
            if (window.ResizeObserver) {
                try {
                    var ro = new ResizeObserver(function () { safeResize(); });
                    ro.observe(el);
                    if (el.parentElement) ro.observe(el.parentElement);
                } catch (e4) {}
            }
            // Re-measure when the parent tab becomes visible (Bootstrap tabs).
            try {
                var tabPane = el.closest && el.closest('.tab-pane');
                if (tabPane) {
                    var observer = new MutationObserver(function () {
                        if (tabPane.classList.contains('active') || tabPane.classList.contains('show')) {
                            safeResize();
                        }
                    });
                    observer.observe(tabPane, { attributes: true, attributeFilter: ['class'] });
                }
            } catch (e5) {}
        }
        return chart;
    }

    function render(el) {
        if (!el || el.__sankeyRendered) return;
        // Phase 20.8.3 — only flip the rendered flag after the draw succeeds. Marking
        // it true up-front meant that a transient fetch / ECharts CDN failure would
        // permanently block any subsequent render attempt (so the preview stayed
        // blank even after the network recovered).
        el.innerHTML = '<div class="text-muted small" style="padding:18px;text-align:center;">جارٍ تحميل المخطط…</div>';
        try {
            fetch('/api/strategy/sankey', { headers: { 'Accept': 'application/json' } })
                .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
                .then(function (data) {
                    if (!data || !data.nodes || !data.nodes.length) { empty(el); return; }
                    loadEcharts(function (err) {
                        if (err || !window.echarts) { fail(el); return; }
                        el.innerHTML = '';
                        if (data.empty) {
                            var warn = document.createElement('div');
                            warn.className = 'alert alert-warning mb-2';
                            warn.textContent = data.warning
                                || 'لا توجد بيانات استراتيجية. يرجى مزامنة MSSQL أو التواصل مع المسؤول.';
                            el.appendChild(warn);
                        }
                        // Phase 20.9 — Strategic is the default per spec.
                        var currentKind = 'strategic';
                        var toolbar = buildToolbar(currentKind);
                        el.appendChild(toolbar);

                        // Horizontal-scroll wrapper so dense graphs don't break the
                        // page layout on phones/tablets.
                        var scroller = document.createElement('div');
                        scroller.className = 'sankey-scroller';
                        scroller.style.cssText = 'width:100%;overflow-x:auto;overflow-y:hidden;-webkit-overflow-scrolling:touch;border:1px solid #e5e9f2;border-radius:8px;background:#fff;';
                        el.appendChild(scroller);

                        var chartHost = document.createElement('div');
                        chartHost.style.minHeight = '600px';
                        scroller.appendChild(chartHost);

                        var currentChart = null;
                        function repaint() {
                            try { if (currentChart) currentChart.dispose(); } catch (eDispose) {}
                            chartHost.innerHTML = '';
                            chartHost.removeAttribute('_echarts_instance_');
                            var filtered = filterData(data, currentKind);
                            if (!filtered.nodes.length) {
                                chartHost.innerHTML = '<div class="text-muted" style="padding:24px;text-align:center;">لا توجد مشاريع ضمن هذا التصنيف.</div>';
                                return;
                            }
                            try { currentChart = draw(chartHost, filtered); }
                            catch (eDraw) { fail(el); }
                        }

                        toolbar.addEventListener('click', function (ev) {
                            var b = ev.target;
                            while (b && b !== toolbar && !(b.getAttribute && b.getAttribute('data-kind'))) b = b.parentNode;
                            if (!b || b === toolbar) return;
                            var kind = b.getAttribute('data-kind');
                            if (!kind) return;
                            currentKind = kind;
                            var btns = toolbar.querySelectorAll('.sankey-filter-btn');
                            for (var i = 0; i < btns.length; i++) {
                                var on = btns[i].getAttribute('data-kind') === currentKind;
                                btns[i].classList.toggle('active', on);
                                btns[i].style.background = on ? '#067647' : '#fff';
                                btns[i].style.color = on ? '#fff' : '#067647';
                            }
                            repaint();
                        });

                        repaint();
                        el.__sankeyRendered = true;
                    });
                })
                .catch(function () { fail(el); });
        } catch (e) { fail(el); }
    }

    // Phase 20.9 — Strategic / Operational / All toggle bar.
    function buildToolbar(currentKind) {
        var bar = document.createElement('div');
        bar.className = 'sankey-toolbar';
        bar.setAttribute('role', 'toolbar');
        bar.style.cssText = 'display:flex;flex-wrap:wrap;gap:8px;justify-content:flex-end;margin-bottom:10px;';

        var label = document.createElement('span');
        label.style.cssText = 'flex:1 1 auto;align-self:center;font-family:Cairo,sans-serif;font-weight:700;color:#28334A;';
        label.textContent = 'عرض المشاريع:';
        bar.appendChild(label);

        function btn(kind, text) {
            var b = document.createElement('button');
            b.type = 'button';
            b.className = 'btn btn-sm sankey-filter-btn' + (kind === currentKind ? ' active' : '');
            b.setAttribute('data-kind', kind);
            b.textContent = text;
            var on = kind === currentKind;
            b.style.cssText = 'font-family:Cairo,sans-serif;font-weight:600;padding:6px 14px;border-radius:8px;border:1px solid #067647;' +
                (on ? 'background:#067647;color:#fff;' : 'background:#fff;color:#067647;');
            return b;
        }
        bar.appendChild(btn('strategic', 'استراتيجية'));
        bar.appendChild(btn('operational', 'تشغيلية'));
        bar.appendChild(btn('all', 'الكل'));
        return bar;
    }

    function fail(el) {
        el.__sankeyRendered = false;
        el.innerHTML = '<div class="alert alert-warning">تعذّر تحميل مخطط التدفق. حاول مرة أخرى لاحقاً.</div>';
    }

    function empty(el) {
        el.__sankeyRendered = false;
        el.innerHTML = '<div class="text-muted" style="padding:18px;text-align:center;">لا توجد بيانات لعرضها حالياً.</div>';
    }

    window.StrategySankey = window.StrategySankey || {};
    window.StrategySankey.render = render;
})();
