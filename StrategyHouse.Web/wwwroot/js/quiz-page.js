// Phase 19.17 — standalone quiz page logic, moved OUT of Views/Quiz/Start.cshtml
// to eliminate Razor/inline-script edge cases that left iPad Safari stuck on the
// «جارٍ تحميل الأسئلة…» spinner. The script reads its data from a JSON block
// (#quiz-data) and data-* attributes on the .quiz-card element, then boots with a
// robust strategy: DOMContentLoaded/immediate boot, bfcache pageshow re-init, and
// a 2s watchdog that re-boots once if the spinner is still visible.
//
// iOS Safari-safe ES5 ONLY: IIFE, var, classic for-loops, string concatenation.
// No template literals, no arrow fns, no let/const, no optional chaining, no ??.
(function () {
    function $(id) { return document.getElementById(id); }

    function esc(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c];
        });
    }

    // --- Read data from the DOM (no Razor interpolation in this file) ---
    var card = document.querySelector('.quiz-card');
    var dataEl = $('quiz-data');
    var data = {};
    try {
        if (dataEl) data = JSON.parse(dataEl.textContent || dataEl.innerText || '{}');
    } catch (e) {
        data = {};
    }

    var questions = data.questions || [];
    var SURVEY = data.survey || { url: '', title: 'شكراً لمشاركتك', body: '', qr: '' };

    var sessionId = card ? card.getAttribute('data-session-id') : null;
    if (sessionId === '' || sessionId === 'null') sessionId = null;
    var scope = (card && card.getAttribute('data-scope')) ? card.getAttribute('data-scope') : 'General';
    var deptCode = card ? card.getAttribute('data-dept-code') : null;
    if (deptCode === '' || deptCode === 'null') deptCode = null;

    var answers = new Array(questions.length);
    for (var k = 0; k < questions.length; k++) answers[k] = -1;
    var cur = 0;

    function setStatus(t) { var s = $('quizStatus'); if (s) s.textContent = t || ''; }

    function render() {
        var area = $('quizArea');
        var bar = $('bar');
        if (!area) { setStatus('تعذّر العثور على منطقة الأسئلة'); return; }
        if (!questions || questions.length === 0) {
            area.innerHTML = '<div class="alert alert-warning">لا توجد أسئلة متاحة الآن.</div>';
            setStatus('');
            return;
        }
        var q = questions[cur];
        if (bar) bar.style.width = Math.round((cur) / questions.length * 100) + '%';
        var html = '';
        html += '<div class="quiz-step">سؤال ' + (cur + 1) + ' من ' + questions.length + '</div>';
        html += '<h3 class="quiz-question">' + esc(q.text) + '</h3>';
        html += '<div class="quiz-options">';
        for (var i = 0; i < q.options.length; i++) {
            var checkedAttr = answers[cur] === i ? 'checked' : '';
            var activeCls = answers[cur] === i ? ' is-selected' : '';
            html += '<label class="quiz-option' + activeCls + '">'
                 + '<input type="radio" name="opt" value="' + i + '" ' + checkedAttr + ' onclick="quizPick(' + i + ')">'
                 + '<span>' + esc(q.options[i]) + '</span></label>';
        }
        html += '</div>';
        html += '<div class="quiz-nav sticky-bottom-cta">';
        html += cur > 0
            ? '<button type="button" class="quiz-btn quiz-btn--ghost" onclick="quizPrev()">السابق</button>'
            : '<span></span>';
        html += cur < questions.length - 1
            ? '<button type="button" class="quiz-btn quiz-btn--primary" onclick="quizNext()">التالي</button>'
            : '<button type="button" class="quiz-btn quiz-btn--primary" onclick="quizSubmit()">إرسال الإجابات</button>';
        html += '</div>';
        area.innerHTML = html;
        setStatus('');
    }

    function pick(i) { answers[cur] = i; render(); }
    function next() { if (cur < questions.length - 1) { cur++; render(); } }
    function prev() { if (cur > 0) { cur--; render(); } }

    function submit() {
        setStatus('جارٍ إرسال الإجابات…');
        var body = {
            sessionId: sessionId,
            scope: scope,
            deptCode: deptCode,
            answers: questions.map(function (q, i) { return { qid: q.id, picked: answers[i] }; })
        };
        fetch('/Quiz/Submit', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        }).then(function (res) { return res.json(); }).then(function (data) {
            var bar = $('bar'); if (bar) bar.style.width = '100%';
            var area = $('quizArea'); if (area) area.style.display = 'none';
            var r = $('resultArea'); if (!r) return;
            r.style.display = 'block';
            var html = '<div class="quiz-score"><div class="quiz-score__label">نتيجتك</div>'
                     + '<div class="quiz-score__value">' + data.score + ' / ' + data.total + '</div></div>';
            for (var i = 0; i < data.detail.length; i++) {
                var d = data.detail[i];
                var q = null;
                for (var j = 0; j < questions.length; j++) { if (questions[j].id === d.qid) { q = questions[j]; break; } }
                if (!q) continue;
                var picked = d.picked >= 0 ? esc(q.options[d.picked]) : '— لم تتم الإجابة —';
                var correct = esc(q.options[d.correctIndex]);
                html += '<div class="quiz-review ' + (d.correct ? 'is-correct' : 'is-wrong') + '">'
                      + '<div class="quiz-review__q">' + esc(q.text) + '</div>'
                      + '<div class="quiz-review__a">إجابتك: ' + picked + ' ' + (d.correct ? '✓' : '✗') + '</div>'
                      + (d.correct ? '' : '<div class="quiz-review__correct">الصحيح: ' + correct + '</div>')
                      + (d.explanation ? '<div class="quiz-muted">' + esc(d.explanation) + '</div>' : '')
                      + '</div>';
            }
            html += '<div class="quiz-nav quiz-nav--center"><button type="button" class="quiz-btn quiz-btn--primary" onclick="quizFinish()">إنهاء</button></div>';
            r.innerHTML = html;
            setStatus('');
        }).catch(function (err) {
            setStatus('');
            var area = $('quizArea');
            if (area) area.innerHTML = '<div class="alert alert-danger">تعذّر إرسال الإجابات. حاول مرة أخرى.</div>';
        });
    }

    function finish() {
        var card = document.querySelector('.quiz-card');
        if (!card) return;
        var s = (typeof SURVEY !== 'undefined' && SURVEY) ? SURVEY : { url: '', title: 'شكراً لمشاركتك', body: '', qr: '' };
        var html = ''
            + '<div class="quiz-thanks">'
            +   '<div class="quiz-thanks__icon" aria-hidden="true">'
            +     '<svg width="56" height="56" viewBox="0 0 24 24" fill="none">'
            +       '<circle cx="12" cy="12" r="11" fill="#067647" />'
            +       '<path d="M7 12.5l3.5 3.5L17 9" stroke="#fff" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" fill="none" />'
            +     '</svg>'
            +   '</div>'
            +   '<h2 class="quiz-thanks__title"></h2>'
            +   '<p class="quiz-thanks__body"></p>';
        if (s.qr) {
            html += '<div class="quiz-thanks__qr"><img alt="رمز الاستبيان" src="' + s.qr + '" /></div>';
        }
        if (s.url) {
            html += '<a class="quiz-thanks__link quiz-btn quiz-btn--primary" target="_blank" rel="noopener" href="' + s.url + '">افتح الاستبيان</a>';
        }
        html += '<button type="button" class="quiz-btn quiz-btn--ghost" style="margin-top:10px;" onclick="location.href=\'/\'">العودة للرئيسية</button>';
        html += '</div>';
        card.innerHTML = html;
        var t = card.querySelector('.quiz-thanks__title');
        var b = card.querySelector('.quiz-thanks__body');
        if (t) t.textContent = s.title || 'شكراً لمشاركتك';
        if (b) b.textContent = s.body || '';
    }

    // Expose to global scope so iOS Safari inline onclick can resolve them.
    window.quizPick = pick;
    window.quizNext = next;
    window.quizPrev = prev;
    window.quizFinish = finish;
    window.quizSubmit = submit;

    function boot() { render(); }

    function tryBoot() {
        try { boot(); } catch (e) {
            var a = $('quizArea');
            if (a) a.innerHTML = '<div class="alert alert-danger">تعذّر تحميل الاختبار: ' + esc(String(e)) + '. <button type="button" class="quiz-btn quiz-btn--ghost" onclick="location.reload()">تحديث الصفحة</button></div>';
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', tryBoot);
    } else {
        tryBoot();
    }

    // bfcache restore (iOS Safari serves a frozen page on back/forward).
    window.addEventListener('pageshow', function (ev) {
        if (ev.persisted) tryBoot();
    });

    // Watchdog: if after 2s the loading spinner is still visible, re-boot once.
    setTimeout(function () {
        var ql = $('quizLoading');
        if (ql && ql.offsetParent !== null) {
            if (window.console) console.log('[quiz] watchdog re-boot');
            tryBoot();
        }
    }, 2000);
})();
