(function () {
  "use strict";

  function normalizeWebPath(p) {
    if (!p || !String(p).trim()) return "index.html";
    var s = String(p).trim().replace(/\\/g, "/").replace(/^\/+/g, "");
    return s || "index.html";
  }

  function webDirSegments(webPathToFile) {
    var i = webPathToFile.lastIndexOf("/");
    if (i <= 0) return [];
    return webPathToFile.substring(0, i).split("/").filter(Boolean);
  }

  function webFileName(webPath) {
    var i = webPath.lastIndexOf("/");
    return i < 0 ? webPath : webPath.substring(i + 1);
  }

  function relFromTo(currentPageWebPath, targetWebPathFromRoot) {
    currentPageWebPath = normalizeWebPath(currentPageWebPath);
    targetWebPathFromRoot = normalizeWebPath(targetWebPathFromRoot);
    var fromParts = webDirSegments(currentPageWebPath);
    var toParts = webDirSegments(targetWebPathFromRoot);
    var toFile = webFileName(targetWebPathFromRoot) || "index.html";
    var i = 0;
    while (
      i < fromParts.length &&
      i < toParts.length &&
      fromParts[i].toLowerCase() === toParts[i].toLowerCase()
    ) {
      i++;
    }
    var up = fromParts.length - i;
    var sb = "";
    for (var u = 0; u < up; u++) sb += "../";
    for (var j = i; j < toParts.length; j++) sb += toParts[j] + "/";
    sb += toFile;
    return sb;
  }

  function esc(s) {
    if (s == null || s === "") return "";
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function escAttr(s) {
    return esc(s).replace(/'/g, "&#39;");
  }

  function escReg(s) {
    return String(s).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  }

  function weight(count, minC, maxC) {
    if (maxC <= minC) return 0.55;
    var lc = Math.log(Math.max(1, count));
    var lo = Math.log(Math.max(1, minC));
    var hi = Math.log(Math.max(1, maxC));
    return Math.max(0, Math.min(1, (lc - lo) / (hi - lo)));
  }

  function mark(text, token) {
    if (!token) return esc(text);
    var isLatin = /[a-zA-Z]/.test(token);
    var re = new RegExp("(" + escReg(token) + ")", isLatin ? "ig" : "g");
    return esc(text).replace(re, '<mark class="wordfreq-hit">$1</mark>');
  }

  function render(data, pagePath) {
    var host = document.getElementById("wordfreq-page-host");
    if (!host) return;

    var stats =
      '<p class="wordfreq-stats">' +
      esc(String(data.articleCount)) +
      " 篇文档 · " +
      esc(String(data.totalTokenOccurrences)) +
      " 次词命中 · " +
      esc(String(data.uniqueTokens)) +
      " 个不同词形 · 本页列出前 " +
      esc(String((data.topTerms || []).length)) +
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
      var wf = weight(t.count, data.minCount, data.maxCount).toFixed(3);
      html +=
        '<button type="button" class="wordfreq-chip" data-token="' +
        escAttr(t.token) +
        '" style="--wf:' +
        wf +
        '">' +
        esc(t.token) +
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

    function activate(token) {
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
          var href = relFromTo(pagePath, r.href || "");
          var snippets = (r.snippets || [])
            .map(function (s) {
              return '<p class="wordfreq-snippet">' + mark(s, token) + "</p>";
            })
            .join("");
          return (
            '<article class="wordfreq-hit-item"><h3 class="wordfreq-hit-title"><a href="' +
            escAttr(href) +
            '">' +
            esc(r.title || "") +
            "</a></h3>" +
            snippets +
            "</article>"
          );
        })
        .join("");
      list.innerHTML = h;
      list.removeAttribute("hidden");
      empty.setAttribute("hidden", "hidden");
      list.scrollIntoView({ behavior: "smooth", block: "start" });
    }

    triggers.forEach(function (el) {
      el.addEventListener("click", function () {
        activate(el.getAttribute("data-token") || "");
      });
    });
  }

  function boot() {
    var host = document.getElementById("wordfreq-page-host");
    if (!host) return;
    var pagePath = document.body.getAttribute("data-page-path") || "词频.html";
    var url = relFromTo(pagePath, "data/word-frequency.json");
    fetch(url, { credentials: "same-origin" })
      .then(function (r) {
        if (!r.ok) throw new Error("bad");
        return r.json();
      })
      .then(function (data) {
        render(data, pagePath);
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
