// Phase 19 — inline quiz used inside the journey (stage 5 "الأثر").
// iOS Safari-safe: ES5 only, IIFE, string concat, for-loops, no template
// literals, no block scope, try/catch with visible fallback. Initialised by
// window.StrategyInlineQuiz.init(opts) where opts = {
//   mount: containerEl, sessionId: string|null, scope: string,
//   deptCode: string|null, onComplete: function }.
(function () {
    function esc(s) {
        return String(s).replace(/[&<>"']/g, function (c) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c];
        });
    }

    function init(opts) {
        opts = opts || {};
        var mount = opts.mount;
        if (!mount || mount.__inlineQuizBound) return;
        mount.__inlineQuizBound = true;

        var sessionId = opts.sessionId || null;
        var scope = opts.scope || 'General';
        var deptCode = opts.deptCode || null;
        var onComplete = typeof opts.onComplete === 'function' ? opts.onComplete : function () {};

        var questions = [];
        var answers = [];
        var cur = 0;

        mount.innerHTML = '<div class="quiz-step">جارٍ تحميل الأسئلة…</div>';

        function load() {
            try {
                fetch('/Quiz/Questions?count=5', { headers: { 'Accept': 'application/json' } })
                    .then(function (r) { return r.ok ? r.json() : Promise.reject(); })
                    .then(function (data) {
                        questions = (data && data.questions) ? data.questions : [];
                        if (!questions.length) { fail(); return; }
                        answers = new Array(questions.length);
                        for (var k = 0; k < questions.length; k++) answers[k] = -1;
                        cur = 0;
                        render();
                    })
                    .catch(fail);
            } catch (e) { fail(); }
        }

        function fail() {
            mount.innerHTML = '<div class="alert alert-danger">تعذّر تحميل الأسئلة. حدّث الصفحة وحاول مرة أخرى.</div>';
        }

        function render() {
            var q = questions[cur];
            if (!q) { fail(); return; }
            var html = '';
            html += '<div class="quiz-step">سؤال ' + (cur + 1) + ' من ' + questions.length + '</div>';
            html += '<div class="quiz-progress"><div class="quiz-progress__fill" style="width:' + Math.round(cur / questions.length * 100) + '%"></div></div>';
            html += '<h3 class="quiz-question">' + esc(q.text) + '</h3>';
            html += '<div class="quiz-options">';
            for (var i = 0; i < q.options.length; i++) {
                var sel = answers[cur] === i;
                html += '<label class="quiz-option' + (sel ? ' is-selected' : '') + '">'
                     + '<input type="radio" name="iq_opt" value="' + i + '" ' + (sel ? 'checked' : '') + ' onclick="window.StrategyInlineQuiz.pick(' + i + ')">'
                     + '<span>' + esc(q.options[i]) + '</span></label>';
            }
            html += '</div>';
            html += '<div class="quiz-nav">';
            html += cur > 0
                ? '<button type="button" class="quiz-btn quiz-btn--ghost" onclick="window.StrategyInlineQuiz.prev()">السابق</button>'
                : '<span></span>';
            html += cur < questions.length - 1
                ? '<button type="button" class="quiz-btn quiz-btn--primary" onclick="window.StrategyInlineQuiz.next()">التالي</button>'
                : '<button type="button" class="quiz-btn quiz-btn--primary" onclick="window.StrategyInlineQuiz.submit()">إرسال الإجابات</button>';
            html += '</div>';
            mount.innerHTML = html;
        }

        function pick(i) { answers[cur] = i; render(); }
        function next() { if (cur < questions.length - 1) { cur++; render(); } }
        function prev() { if (cur > 0) { cur--; render(); } }

        function submit() {
            var body = {
                sessionId: sessionId,
                scope: scope,
                deptCode: deptCode,
                answers: questions.map(function (q, i) { return { qid: q.id, picked: answers[i] }; })
            };
            mount.innerHTML = '<div class="quiz-step">جارٍ احتساب نتيجتك…</div>';
            try {
                fetch('/Quiz/Submit', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(body)
                }).then(function (res) { return res.ok ? res.json() : Promise.reject(); })
                  .then(function (data) { showResult(data); })
                  .catch(function () { mount.innerHTML = '<div class="alert alert-danger">تعذّر إرسال الإجابات. حاول مرة أخرى.</div>'; });
            } catch (e) {
                mount.innerHTML = '<div class="alert alert-danger">تعذّر إرسال الإجابات. حاول مرة أخرى.</div>';
            }
        }

        function showResult(data) {
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
            mount.innerHTML = html;
            try { onComplete(data); } catch (e) {}
        }

        // Expose handlers globally so inline onclick can resolve on iOS Safari.
        window.StrategyInlineQuiz.pick = pick;
        window.StrategyInlineQuiz.next = next;
        window.StrategyInlineQuiz.prev = prev;
        window.StrategyInlineQuiz.submit = submit;

        load();
    }

    window.StrategyInlineQuiz = window.StrategyInlineQuiz || {};
    window.StrategyInlineQuiz.init = init;
})();
</content>
</invoke>
