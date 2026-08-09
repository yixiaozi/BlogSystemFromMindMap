(function (global) {
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
    return bustAsset(sb);
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

  function parseTermParam() {
    var raw = "";
    try {
      var u = new URL(global.location.href);
      raw = u.searchParams.get("term") || u.searchParams.get("word") || "";
    } catch (e) {}
    if (!raw && global.location.search) {
      var m = /[?&](?:term|word)=([^&]*)/i.exec(global.location.search);
      if (m) {
        try {
          raw = decodeURIComponent(m[1].replace(/\+/g, " "));
        } catch (e2) {
          raw = m[1];
        }
      }
    }
    return normalizeTerm(raw);
  }

  function normalizeTerm(s) {
    return String(s || "")
      .trim()
      .normalize("NFC");
  }

  function resolveTermToken(requested, candidates) {
    var want = normalizeTerm(requested);
    if (!want) return "";
    for (var i = 0; i < candidates.length; i++) {
      var c = normalizeTerm(candidates[i]);
      if (c === want) return candidates[i];
    }
    return "";
  }

  function buildWordFreqPageUrl(currentPageWebPath, wordFreqPageWebPath, term) {
    var href = relFromTo(currentPageWebPath, wordFreqPageWebPath);
    if (term) href += "?term=" + encodeURIComponent(term);
    return href;
  }

  global.MindmapBlogWordFreq = {
    normalizeWebPath: normalizeWebPath,
    relFromTo: relFromTo,
    esc: esc,
    escAttr: escAttr,
    escReg: escReg,
    weight: weight,
    mark: mark,
    parseTermParam: parseTermParam,
    normalizeTerm: normalizeTerm,
    resolveTermToken: resolveTermToken,
    buildWordFreqPageUrl: buildWordFreqPageUrl,
  };
})(typeof window !== "undefined" ? window : this);
