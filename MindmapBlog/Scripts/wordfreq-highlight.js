(function () {
  "use strict";

  var WF = window.MindmapBlogWordFreq;
  if (!WF) return;

  var SKIP_ANCESTOR =
    "script,style,textarea,noscript,code,pre," +
    ".article-outline-dock,.wordfreq-cloud,.wordfreq-chip,.wordfreq-hits,.wordfreq-hits-list," +
    ".rev-dock,.site-topbar,.site-footer,button,input,select,textarea," +
    "a,.wordfreq-inline,.wordfreq-hit,mark";

  function ready(fn) {
    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", fn);
    } else {
      fn();
    }
  }

  function overlaps(ranges, start, end) {
    for (var i = 0; i < ranges.length; i++) {
      var r = ranges[i];
      if (start < r[1] && end > r[0]) return true;
    }
    return false;
  }

  function findMatches(text, terms) {
    var ranges = [];
    var matches = [];
    for (var ti = 0; ti < terms.length; ti++) {
      var term = terms[ti];
      if (!term) continue;
      var isLatin = /[a-zA-Z]/.test(term);
      var hay = isLatin ? text.toLowerCase() : text;
      var needle = isLatin ? term.toLowerCase() : term;
      var pos = 0;
      while (pos < hay.length) {
        var idx = hay.indexOf(needle, pos);
        if (idx === -1) break;
        var end = idx + term.length;
        if (!overlaps(ranges, idx, end)) {
          ranges.push([idx, end]);
          matches.push({ start: idx, end: end, term: text.substring(idx, end) });
        }
        pos = idx + 1;
      }
    }
    matches.sort(function (a, b) {
      return a.start - b.start;
    });
    return matches;
  }

  function wrapMatches(text, matches, pagePath, wordFreqPagePath) {
    if (!matches.length) return document.createTextNode(text);
    var frag = document.createDocumentFragment();
    var cursor = 0;
    for (var i = 0; i < matches.length; i++) {
      var m = matches[i];
      if (m.start > cursor) {
        frag.appendChild(document.createTextNode(text.substring(cursor, m.start)));
      }
      var a = document.createElement("a");
      a.className = "wordfreq-inline";
      a.href = WF.buildWordFreqPageUrl(pagePath, wordFreqPagePath, m.term);
      a.setAttribute("data-term", m.term);
      a.title = "在词频页查看「" + m.term + "」";
      a.textContent = text.substring(m.start, m.end);
      frag.appendChild(a);
      cursor = m.end;
    }
    if (cursor < text.length) {
      frag.appendChild(document.createTextNode(text.substring(cursor)));
    }
    return frag;
  }

  function shouldSkipTextNode(node) {
    var p = node.parentElement;
    if (!p) return true;
    if (p.closest(SKIP_ANCESTOR)) return true;
    if (p.closest(".layout-nav,.layout-tags")) return true;
    return false;
  }

  function highlightRoot(root, terms, pagePath, wordFreqPagePath) {
    if (!root || root.getAttribute("data-wordfreq-highlighted") === "1") return;
    var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    var nodes = [];
    var n;
    while ((n = walker.nextNode())) {
      if (!shouldSkipTextNode(n)) nodes.push(n);
    }
    for (var i = 0; i < nodes.length; i++) {
      var textNode = nodes[i];
      var text = textNode.nodeValue;
      if (!text || !text.trim()) continue;
      var matches = findMatches(text, terms);
      if (!matches.length) continue;
      var parent = textNode.parentNode;
      if (!parent) continue;
      parent.replaceChild(wrapMatches(text, matches, pagePath, wordFreqPagePath), textNode);
    }
    root.setAttribute("data-wordfreq-highlighted", "1");
  }

  function boot() {
    if (document.body && document.body.getAttribute("data-is-search-page") === "1") return;
    if (document.getElementById("wordfreq-page-root")) return;

    var pagePath =
      (document.body && document.body.getAttribute("data-page-path")) || "index.html";
    var url = WF.relFromTo(pagePath, "data/word-frequency-terms.json");

    fetch(url, { credentials: "same-origin" })
      .then(function (r) {
        if (!r.ok) throw new Error("bad");
        return r.json();
      })
      .then(function (data) {
        var terms = data.terms || [];
        if (!terms.length) return;

        terms.sort(function (a, b) {
          return String(b).length - String(a).length;
        });

        var wordFreqPagePath = data.pageWebPath || "词频.html";
        var main = document.querySelector(".layout-main");
        if (!main) return;

        highlightRoot(main, terms, pagePath, wordFreqPagePath);

        main.addEventListener("click", function (e) {
          var link = e.target.closest("a.wordfreq-inline");
          if (!link) return;
          e.preventDefault();
          window.location.href = link.getAttribute("href") || link.href;
        });
      })
      .catch(function () {
        /* 词频索引缺失时静默跳过 */
      });
  }

  ready(boot);
})();
