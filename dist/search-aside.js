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
    return sb;
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

  function matchHay(hayLower, tms) {
    for (var i = 0; i < tms.length; i++) {
      if (hayLower.indexOf(tms[i]) < 0) return false;
    }
    return tms.length > 0;
  }

  function snippet(hay, tms, maxLen) {
    maxLen = maxLen || 96;
    if (!hay) return "";
    var lower = hay.toLowerCase();
    var pos = lower.length;
    for (var i = 0; i < tms.length; i++) {
      var p = lower.indexOf(tms[i]);
      if (p >= 0 && p < pos) pos = p;
    }
    if (pos >= lower.length) pos = 0;
    var start = Math.max(0, pos - 28);
    var s = hay.substring(start, start + maxLen);
    if (start > 0) s = "\u2026" + s;
    if (start + maxLen < hay.length) s = s + "\u2026";
    return s.replace(/\s+/g, " ").trim();
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

  function render(data, tms) {
    if (tms.length === 0) {
      list.innerHTML = "";
      list.hidden = true;
      status.textContent = "";
      return;
    }
    var hits = [];
    for (var i = 0; i < data.length; i++) {
      var e = data[i];
      var hay = joinAll(e);
      var hayLower = hay.toLowerCase();
      if (matchHay(hayLower, tms)) hits.push(e);
    }
    if (hits.length === 0) {
      list.innerHTML = "";
      list.hidden = true;
      status.textContent = "\u672a\u627e\u5230\u5339\u914d";
      return;
    }
    status.textContent =
      "\u627e\u5230 " +
      hits.length +
      " \u7bc7\uff08\u6700\u591a\u663e\u793a 20 \u6761\uff09";
    list.hidden = false;
    var html = "";
    var maxShow = Math.min(20, hits.length);
    for (var j = 0; j < maxShow; j++) {
      var e = hits[j];
      var href = relFromTo(pagePath, e.href);
      var sn = snippet(joinAll(e), tms, 100);
      html +=
        '<li class="search-hit-item"><a class="search-hit-link" href="' +
        escapeAttr(href) +
        '"><span class="search-hit-title">' +
        escapeHtml(e.title) +
        '</span><span class="search-hit-snippet">' +
        escapeHtml(sn) +
        "</span></a></li>";
    }
    list.innerHTML = html;
  }

  function schedule() {
    if (timer) clearTimeout(timer);
    timer = setTimeout(function () {
      var tms = terms(input.value);
      load(function (data) {
        render(data, tms);
      });
    }, 280);
  }

  input.addEventListener("input", schedule);
  input.addEventListener("search", schedule);
})();
