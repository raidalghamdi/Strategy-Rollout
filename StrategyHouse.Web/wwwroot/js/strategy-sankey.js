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

    function draw(el, data) {
        // Phase 20.1 — height driven by the tallest column, not total node count.
        // Each label needs ~3 lines × ~22px line-height ≈ 66px + gap.
        var tallest = tallestColumn(data);
        var perNode = 72;
        var height = Math.max(1400, tallest * perNode + 120);
        el.style.height = height + 'px';
        // Phase 20.8.3 — do not force a min-width that breaks responsive layouts; let
        // ECharts stretch to the container width and rely on overflow-x on the parent.
        el.style.width = '100%';

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
                left: 12, right: 12, top: 28, bottom: 28,
                data: nodes,
                links: data.links,
                emphasis: { focus: 'adjacency', lineStyle: { opacity: 0.7 } },
                lineStyle: { color: 'gradient', curveness: 0.5, opacity: 0.4 },
                label: {
                    fontFamily: 'Cairo, sans-serif',
                    fontSize: 13,
                    fontWeight: 600,
                    color: '#1a2638',
                    formatter: function (p) { return wrapN(p.name, 24, 3); },
                    overflow: 'break',
                    width: 170,
                    lineHeight: 18
                },
                nodeWidth: 14,
                nodeGap: 16,
                nodeAlign: 'justify',
                draggable: false
            }]
        };
        chart.setOption(option);
        // Phase 20.8.3 — ECharts measures the container at init(); if the tab was
        // hidden a moment ago the canvas can come out 0 × 0. Force a resize once the
        // browser has actually laid the element out so the diagram fills the panel.
        try { setTimeout(function () { try { chart.resize(); } catch (e2) {} }, 60); } catch (e1) {}
        if (!el.__resizeBound) {
            el.__resizeBound = true;
            window.addEventListener('resize', function () { try { chart.resize(); } catch (e) {} });
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
                        var chartHost = document.createElement('div');
                        chartHost.style.width = '100%';
                        chartHost.style.height = '100%';
                        el.appendChild(chartHost);
                        try { draw(chartHost, data); el.__sankeyRendered = true; }
                        catch (e) { fail(el); }
                    });
                })
                .catch(function () { fail(el); });
        } catch (e) { fail(el); }
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
