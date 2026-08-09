(function () {
  function normalizeWebPath(p) {
    if (!p || !String(p).trim()) return "index.html";
    var s = String(p).trim().replace(/\\/g, "/").replace(/^\/+/g, "");
    return s || "index.html";
  }

  function webDirSegments(webPathToFile) {
    var i = webPathToFile.lastIndexOf("/");
    if (i <= 0) return [];
    var dir = webPathToFile.substring(0, i);
    return dir.split("/").filter(Boolean);
  }

  function webFileName(webPath) {
    var i = webPath.lastIndexOf("/");
    return i < 0 ? webPath : webPath.substring(i + 1);
  }

  function bustAsset(url) {
    if (!url || !/\.(json|js|css)(\?|$)/i.test(url)) return url;
    if (/[?&]v=/.test(url)) return url;
    return typeof MindmapBlogBust === "function" ? MindmapBlogBust(url) : url;
  }

  function relFromTo(currentPageWebPath, targetWebPathFromRoot) {
    currentPageWebPath = normalizeWebPath(currentPageWebPath);
    targetWebPathFromRoot = normalizeWebPath(targetWebPathFromRoot);
    var fromParts = webDirSegments(currentPageWebPath);
    var toParts = webDirSegments(targetWebPathFromRoot);
    var toFile = webFileName(targetWebPathFromRoot);
    if (!toFile) toFile = "index.html";
    var i = 0;
    while (
      i < fromParts.length &&
      i < toParts.length &&
      fromParts[i].toLowerCase() === toParts[i].toLowerCase()
    )
      i++;
    var up = fromParts.length - i;
    var sb = "";
    for (var u = 0; u < up; u++) sb += "../";
    for (var j = i; j < toParts.length; j++) sb += toParts[j] + "/";
    sb += toFile;
    return bustAsset(sb);
  }

  function joinAll(e) {
    if (e.all) return e.all;
    var parts = [
      e.title,
      e.body,
      (e.bookmarks || []).join(" "),
      (e.imageAlts || []).join(" "),
      e.section,
      e.notebook,
      e.reminder || "",
      e.sourceFile || "",
    ];
    return parts.filter(Boolean).join("\n");
  }

  function terms(q) {
    return q
      .trim()
      .toLowerCase()
      .split(/\s+/)
      .filter(function (s) {
        return s.length > 0;
      });
  }

  function parseSearchQueryParam() {
    var raw = "";
    try {
      var u = new URL(window.location.href);
      raw =
        u.searchParams.get("q") ||
        u.searchParams.get("term") ||
        u.searchParams.get("query") ||
        "";
    } catch (e) {}
    if (!raw && window.location.search) {
      var m = /[?&](?:q|term|query)=([^&]*)/i.exec(window.location.search);
      if (m) {
        try {
          raw = decodeURIComponent(m[1].replace(/\+/g, " "));
        } catch (e2) {
          raw = m[1];
        }
      }
    }
    return String(raw || "").trim();
  }

  function isSearchPage() {
    return !!(document.body && document.body.getAttribute("data-is-search-page") === "1");
  }

  function syncSearchUrl(query) {
    if (!isSearchPage()) return;
    try {
      var u = new URL(window.location.href);
      query = String(query || "").trim();
      u.searchParams.delete("term");
      u.searchParams.delete("query");
      if (query) u.searchParams.set("q", query);
      else u.searchParams.delete("q");
      var next = u.pathname + u.search + u.hash;
      if (next !== window.location.pathname + window.location.search + window.location.hash) {
        window.history.replaceState(null, "", next);
      }
    } catch (e) {}
  }

  function matchHay(hayLower, tms) {
    for (var i = 0; i < tms.length; i++) {
      if (hayLower.indexOf(tms[i]) < 0) return false;
    }
    return tms.length > 0;
  }

  function normalizeLine(raw) {
    return (raw || "").replace(/\s+/g, " ").trim();
  }

  function displayLine(line, maxLen) {
    maxLen = maxLen || 160;
    line = normalizeLine(line);
    return line.length > maxLen ? line.slice(0, maxLen) + "\u2026" : line;
  }

  function searchLines(e) {
    var lines = [];
    var seen = {};

    function add(raw) {
      var s = normalizeLine(raw);
      if (!s || seen[s]) return;
      seen[s] = true;
      lines.push(s);
    }

    add(e.title);
    if (e.body) {
      e.body.split("\n").forEach(function (raw) {
        add(raw);
      });
    }
    (e.bookmarks || []).forEach(add);
    (e.imageAlts || []).forEach(add);
    add(e.section);
    add(e.notebook);
    add(e.reminder);
    add(e.sourceFile);
    return lines;
  }

  function collectGroupedHits(data, tms) {
    var groups = [];
    for (var i = 0; i < data.length; i++) {
      var e = data[i];
      var hayLower = joinAll(e).toLowerCase();
      if (!matchHay(hayLower, tms)) continue;

      var matched = [];
      var lineSeen = {};
      var lines = searchLines(e);
      for (var j = 0; j < lines.length; j++) {
        var line = lines[j];
        if (!matchHay(line.toLowerCase(), tms)) continue;
        if (lineSeen[line]) continue;
        lineSeen[line] = true;
        matched.push(line);
      }

      if (matched.length === 0) continue;
      groups.push({
        href: e.href,
        title: e.title,
        lines: matched,
      });
    }
    return groups;
  }

  var root = document.getElementById("site-search-aside");
  if (!root) return;
  var indexUrl = root.getAttribute("data-index-href");
  var pagePath = root.getAttribute("data-page-path") || "index.html";
  var input = document.getElementById("site-search-q");
  var list = document.getElementById("site-search-list");
  var status = document.getElementById("site-search-status");
  if (!indexUrl || !input || !list || !status) return;

  var cache = null;
  var timer = null;

  function load(cb) {
    if (cache) {
      cb(cache);
      return;
    }
    status.textContent = "\u52a0\u8f7d\u7d22\u5f15\u2026";
    fetch(indexUrl, { credentials: "same-origin" })
      .then(function (r) {
        if (!r.ok) throw new Error("bad");
        return r.json();
      })
      .then(function (d) {
        cache = d;
        status.textContent = "";
        cb(d);
      })
      .catch(function () {
        status.textContent =
          "\u65e0\u6cd5\u52a0\u8f7d\u641c\u7d22\u7d22\u5f15\uff0c\u8bf7\u91cd\u65b0\u751f\u6210\u7ad9\u70b9\u3002";
      });
  }

  function escapeHtml(s) {
    if (s == null || s === "") return "";
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function escapeAttr(s) {
    return escapeHtml(s).replace(/'/g, "&#39;");
  }

  function escReg(s) {
    return String(s).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  }

  function markTerms(text, tms) {
    var out = escapeHtml(displayLine(text));
    for (var i = 0; i < tms.length; i++) {
      var token = tms[i];
      if (!token) continue;
      var isLatin = /[a-zA-Z]/.test(token);
      var re = new RegExp("(" + escReg(token) + ")", isLatin ? "ig" : "g");
      out = out.replace(re, '<mark class="wordfreq-hit">$1</mark>');
    }
    return out;
  }

  function render(data, tms) {
    if (tms.length === 0) {
      list.innerHTML = "";
      list.hidden = true;
      status.textContent = "";
      return;
    }

    var groups = collectGroupedHits(data, tms);
    if (groups.length === 0) {
      list.innerHTML = "";
      list.hidden = true;
      status.textContent = "\u672a\u627e\u5230\u5339\u914d";
      return;
    }

    var nodeCount = 0;
    for (var g = 0; g < groups.length; g++) nodeCount += groups[g].lines.length;

    status.textContent =
      "\u627e\u5230 " + nodeCount + " \u4e2a\u8282\u70b9\uff08" + groups.length + " \u7bc7\uff09";
    list.hidden = false;

    var html = "";
    for (var i = 0; i < groups.length; i++) {
      var group = groups[i];
      var href = relFromTo(pagePath, group.href);
      html += '<li class="search-hit-group">';
      html +=
        '<a class="search-hit-link search-hit-group-title" href="' +
        escapeAttr(href) +
        '"><span class="search-hit-title">' +
        escapeHtml(group.title) +
        "</span></a>";
      html += '<ul class="search-hit-nodes">';
      for (var j = 0; j < group.lines.length; j++) {
        html +=
          '<li class="search-hit-item"><a class="search-hit-link" href="' +
          escapeAttr(href) +
          '"><span class="search-hit-snippet">' +
          markTerms(group.lines[j], tms) +
          "</span></a></li>";
      }
      html += "</ul></li>";
    }
    list.innerHTML = html;
  }

  function schedule(immediate) {
    if (timer) clearTimeout(timer);
    var run = function () {
      var tms = terms(input.value);
      syncSearchUrl(input.value);
      load(function (data) {
        render(data, tms);
      });
    };
    if (immediate) run();
    else timer = setTimeout(run, 280);
  }

  var initialQuery = parseSearchQueryParam();
  if (initialQuery) {
    input.value = initialQuery;
    schedule(true);
  }

  input.addEventListener("input", function () {
    schedule(false);
  });
  input.addEventListener("search", function () {
    schedule(true);
  });
})();
