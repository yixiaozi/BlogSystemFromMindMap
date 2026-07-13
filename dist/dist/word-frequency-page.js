(function () {
  "use strict";

  var WF = window.MindmapBlogWordFreq;
  if (!WF) return;

  function render(data, pagePath, initialTerm) {
    var host = document.getElementById("wordfreq-page-host");
    if (!host) return;

    var stats =
      '<p class="wordfreq-stats">' +
      WF.esc(String(data.articleCount)) +
      " 篇文档 · " +
      WF.esc(String(data.totalTokenOccurrences)) +
      " 次词命中 · " +
      WF.esc(String(data.uniqueTokens)) +
      " 个不同词形 · 本页列出前 " +
      WF.esc(String((data.topTerms || []).length)) +
      " 个高频词</p>";

    var terms = data.topTerms || [];
    if (!terms.length) {
      host.outerHTML =
        stats + '<p class="wordfreq-empty">暂无可用正文，无法生成词频。</p>';
      return;
    }

    var html = stats + '<div class="wordfreq-cloud" aria-label="词频标签云">';
    for (var i = 0; i < terms.length; i++) {
      var t = terms[i];
      var wf = WF.weight(t.count, data.minCount, data.maxCount).toFixed(3);
      html +=
        '<button type="button" class="wordfreq-chip" data-token="' +
        WF.escAttr(t.token) +
        '" style="--wf:' +
        wf +
        '">' +
        WF.esc(t.token) +
        "</button>";
    }
    html += "</div>";
    html +=
      '<section class="wordfreq-hits" aria-live="polite" aria-label="词条关联文档">';
    html += '<h2 class="wordfreq-chart-title">关联文档与句子</h2>';
    html +=
      '<p id="wordfreq-hits-empty" class="wordfreq-hits-empty">点击上方词条，查看它出现在哪些文档里，以及对应句子。</p>';
    html +=
      '<div id="wordfreq-hits-list" class="wordfreq-hits-list" hidden></div></section>';

    host.outerHTML = html;

    var map = data.hitsByTerm || {};
    var empty = document.getElementById("wordfreq-hits-empty");
    var list = document.getElementById("wordfreq-hits-list");
    var triggers = Array.prototype.slice.call(
      document.querySelectorAll(".wordfreq-chip[data-token]")
    );
    if (!empty || !list || !triggers.length) return;

    function activate(token, scrollHits) {
      if (scrollHits === undefined) scrollHits = true;
      triggers.forEach(function (el) {
        var on = (el.getAttribute("data-token") || "") === token;
        el.classList.toggle("is-active", on);
      });
      var rows = map[token] || [];
      if (!rows.length) {
        list.setAttribute("hidden", "hidden");
        list.innerHTML = "";
        empty.textContent = "“" + token + "” 暂无关联文档。";
        empty.removeAttribute("hidden");
        return;
      }
      var h = rows
        .map(function (r) {
          var href = WF.relFromTo(pagePath, r.href || "");
          var snippets = (r.snippets || [])
            .map(function (s) {
              return '<p class="wordfreq-snippet">' + WF.mark(s, token) + "</p>";
            })
            .join("");
          return (
            '<article class="wordfreq-hit-item"><h3 class="wordfreq-hit-title"><a href="' +
            WF.escAttr(href) +
            '">' +
            WF.esc(r.title || "") +
            "</a></h3>" +
            snippets +
            "</article>"
          );
        })
        .join("");
      list.innerHTML = h;
      list.removeAttribute("hidden");
      empty.setAttribute("hidden", "hidden");
      if (scrollHits) {
        list.scrollIntoView({ behavior: "smooth", block: "start" });
      }
    }

    triggers.forEach(function (el) {
      el.addEventListener("click", function () {
        var token = el.getAttribute("data-token") || "";
        activate(token, true);
        try {
          var u = new URL(window.location.href);
          u.searchParams.set("term", token);
          window.history.replaceState(null, "", u.pathname + u.search + u.hash);
        } catch (e) {}
      });
    });

    var tokens = triggers.map(function (el) {
      return el.getAttribute("data-token") || "";
    });
    var picked = WF.resolveTermToken(initialTerm || WF.parseTermParam(), tokens);
    if (picked) {
      window.requestAnimationFrame(function () {
        activate(picked, true);
        triggers.forEach(function (el) {
          if ((el.getAttribute("data-token") || "") === picked && el.scrollIntoView) {
            el.scrollIntoView({ behavior: "smooth", block: "nearest" });
          }
        });
      });
    }
  }

  function boot() {
    var host = document.getElementById("wordfreq-page-host");
    if (!host) return;
    var pagePath = document.body.getAttribute("data-page-path") || "词频.html";
    var initialTerm = WF.parseTermParam();
    var url = WF.relFromTo(pagePath, "data/word-frequency.json");
    fetch(url, { credentials: "same-origin" })
      .then(function (r) {
        if (!r.ok) throw new Error("bad");
        return r.json();
      })
      .then(function (data) {
        render(data, pagePath, initialTerm);
      })
      .catch(function () {
        host.textContent = "无法加载词频数据，请重新生成站点。";
      });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
