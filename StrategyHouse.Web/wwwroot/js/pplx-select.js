// Phase 20.12 — Tom Select enhancer.
// Any <select> with [data-pplx-search="1"] becomes a searchable combobox.
// Selects with more than 10 <option>s get auto-enhanced even without the
// attribute (so existing admin dropdowns gain search without code changes).
// RTL is forced via dir="rtl" and a small CSS shim (phase20_12-pplx-select.css).
(function () {
    'use strict';
    if (!window.TomSelect) return;

    function pickPlaceholder(sel) {
        var explicit = sel.getAttribute('data-placeholder');
        if (explicit) return explicit;
        var blank = sel.querySelector('option[value=""]');
        if (blank && blank.textContent.trim()) return blank.textContent.trim();
        return 'ابحث...';
    }

    function shouldEnhance(sel) {
        if (sel.tagName !== 'SELECT') return false;
        if (sel.tomselect) return false;
        if (sel.multiple) {
            // Tom Select supports multi but the platform has none yet — leave alone.
            return false;
        }
        if (sel.hasAttribute('data-pplx-noselect')) return false;
        if (sel.getAttribute('data-pplx-search') === '1') return true;
        // Auto-enhance any select with more than 10 options.
        if (sel.options && sel.options.length > 10) return true;
        return false;
    }

    function enhance(sel) {
        if (!shouldEnhance(sel)) return null;
        try {
            var placeholder = pickPlaceholder(sel);
            var ts = new TomSelect(sel, {
                allowEmptyOption: true,
                create: false,
                maxOptions: 500,
                placeholder: placeholder,
                searchField: ['text', 'value'],
                // RTL-friendly diacritic-insensitive search
                diacritics: true,
                render: {
                    no_results: function (data, escape) {
                        return '<div class="no-results">لا توجد نتائج لـ "' + escape(data.input) + '"</div>';
                    }
                }
            });
            // Force RTL on the generated wrapper.
            if (ts.wrapper) {
                ts.wrapper.setAttribute('dir', 'rtl');
                ts.wrapper.classList.add('pplx-ts');
            }
            return ts;
        } catch (e) {
            // Silent fail — leave the native <select> usable.
            return null;
        }
    }

    function enhanceAll(scope) {
        var root = scope || document;
        var selects = root.querySelectorAll('select');
        for (var i = 0; i < selects.length; i++) enhance(selects[i]);
    }

    // Expose the per-element enhancer so dynamic code (e.g. Stage 5's
    // project-dropdown rebuild) can re-bind after replacing <options>.
    window.PplxEnhanceSelect = enhance;
    window.PplxEnhanceAllSelects = enhanceAll;

    function init() {
        enhanceAll(document);
        // Hook the journey "stageReady" event so each stage's selects are
        // enhanced as soon as the partial is injected into the DOM.
        document.addEventListener('stageReady', function () {
            enhanceAll(document);
        });
    }

    if (document.readyState !== 'loading') init();
    else document.addEventListener('DOMContentLoaded', init);
})();
