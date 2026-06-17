// Phase 19 — interactive strategy flow (Sankey) for stage 2 "تدفق تفاعلي" tab.
// Uses ECharts (loaded lazily from CDN). iOS Safari-safe: ES5 only, IIFE, string
// concat, for-loops, no template literals, no block scope, try/catch + fallback.
// Public API: window.StrategySankey.render(containerEl).
(function () {
    var ECHARTS_SRC = 'https://cdn.jsdelivr.net/npm/echarts@5.5.0/dist/echarts.min.js';
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
            case 'pillar': return '#067647';
            case 'objective': return '#299ECE';
            case 'initiative': return '#FAC126';
            case 'project': return '#28334A';
            default: return '#5a6b80';
        }
    }

    function draw(el, data) {
        var chart = window.echarts.init(el, null, { renderer: 'canvas' });
        var nodes = [];
        for (var i = 0; i < data.nodes.length; i++) {
            var n = data.nodes[i];
            nodes.push({
                name: n.name,
                itemStyle: { color: colorFor(n.category) }
            });
        }
        var option = {
            tooltip: { trigger: 'item', triggerOn: 'mousemove' },
            series: [{
                type: 'sankey',
                left: 8, right: 8, top: 12, bottom: 12,
                data: nodes,
                links: data.links,
                emphasis: { focus: 'adjacency' },
                lineStyle: { color: 'gradient', curveness: 0.5, opacity: 0.45 },
                label: {
                    fontFamily: 'Cairo, sans-serif',
                    fontSize: 12,
                    color: '#28334A'
                },
                nodeWidth: 16,
                nodeGap: 10
            }]
        };
        chart.setOption(option);
        // Keep the chart responsive to container/orientation changes.
        if (!el.__resizeBound) {
            el.__resizeBound = true;
            window.addEventListener('resize', function () { try { chart.resize(); } catch (e) {} });
        }
        return chart;
    }

    function render(el) {
        if (!el || el.__sankeyRendered) return;
        el.__sankeyRendered = true;
        el.innerHTML = '<div class="text-muted small" style="padding:18px;text-align:center;">جارٍ تحميل المخطط…</div>';
        try {
            fetch('/api/strategy/sankey', { headers: { 'Accept': 'application/json' } })
                .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
                .then(function (data) {
                    if (!data || !data.nodes || !data.nodes.length) { fail(el); return; }
                    loadEcharts(function (err) {
                        if (err || !window.echarts) { fail(el); return; }
                        el.innerHTML = '';
                        if (data.dummy) {
                            var warn = document.createElement('div');
                            warn.className = 'alert alert-warning mb-2';
                            warn.textContent = data.warning
                                || 'البيانات تجريبية — يرجى الضغط على زر دفع البيانات في صفحة الإدارة.';
                            el.appendChild(warn);
                        }
                        var chartHost = document.createElement('div');
                        chartHost.style.width = '100%';
                        chartHost.style.height = '100%';
                        el.appendChild(chartHost);
                        try { draw(chartHost, data); }
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

    window.StrategySankey = window.StrategySankey || {};
    window.StrategySankey.render = render;
})();
