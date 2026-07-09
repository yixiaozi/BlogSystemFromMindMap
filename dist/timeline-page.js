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

  function pad2(n) {
    return n < 10 ? "0" + n : String(n);
  }

  function formatDateLabel(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    return d.getFullYear() + "年" + (d.getMonth() + 1) + "月" + d.getDate() + "日";
  }

  function formatClock(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    return pad2(d.getHours()) + ":" + pad2(d.getMinutes());
  }

  function formatMeta(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    return (
      d.getFullYear() +
      "-" +
      pad2(d.getMonth() + 1) +
      "-" +
      pad2(d.getDate()) +
      " " +
      pad2(d.getHours()) +
      ":" +
      pad2(d.getMinutes())
    );
  }

  function renderItems(items, pagePath, sortIsoKey) {
    var html = "";
    var prevDate = null;
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      var iso = it[sortIsoKey] || it.published;
      var d = new Date(iso);
      var same =
        prevDate &&
        d.getFullYear() === prevDate.getFullYear() &&
        d.getMonth() === prevDate.getMonth() &&
        d.getDate() === prevDate.getDate();
      prevDate = d;

      html +=
        '<li class="timeline-item" data-published="' +
        escAttr(it.published) +
        '" data-modified="' +
        escAttr(it.modified) +
        '">';
      html += '<div class="timeline-lead"><time class="timeline-datetime" datetime="' + escAttr(iso) + '">';
      if (same) {
        html +=
          '<span class="timeline-date timeline-date-repeat" aria-hidden="true"></span>';
      } else {
        html += '<span class="timeline-date">' + esc(formatDateLabel(iso)) + "</span>";
      }
      html +=
        '<div class="timeline-clock-row"><span class="timeline-clock">' +
        esc(formatClock(iso)) +
        '</span><span class="timeline-marker" aria-hidden="true"><span class="timeline-dot"></span></span></div>';
      html += "</time></div>";
      html += '<article class="timeline-card"><div class="timeline-head">';
      html +=
        '<h2 class="timeline-title"><a href="' +
        escAttr(relFromTo(pagePath, it.href)) +
        '">' +
        esc(it.title) +
        "</a></h2>";
      html += '<div class="timeline-bm">';
      var bms = it.bookmarks || [];
      var bmPages = it.bookmarkPages || {};
      for (var b = 0; b < bms.length; b++) {
        var bm = bms[b];
        var tagHref = bmPages[bm] ? relFromTo(pagePath, bmPages[bm]) : "#";
        html +=
          '<a class="bm-pill sm" href="' +
          escAttr(tagHref) +
          '">' +
          esc(bm) +
          "</a>";
      }
      html += "</div></div>";
      html +=
        '<p class="timeline-meta"><span class="timeline-meta-item" title="首次进入思维导图博客并发布的时间">入站 ' +
        esc(formatMeta(it.published)) +
        '</span><span class="timeline-meta-sep" aria-hidden="true">·</span><span class="timeline-meta-item" title="思维导图节点最后修改时间">更新 ' +
        esc(formatMeta(it.modified)) +
        "</span></p>";
      if (it.excerpt) {
        html += '<p class="timeline-excerpt">' + esc(it.excerpt) + "</p>";
      }
      html += "</article></li>";
    }
    return html;
  }

  function renderTimeline(data, pagePath) {
    var host = document.getElementById("timeline-page-host");
    var root = document.getElementById("timeline-page-root");
    var titleEl = document.getElementById("timeline-page-title");
    var subEl = document.getElementById("timeline-page-sub");
    var leadEl = document.getElementById("timeline-page-lead");
    if (!host) return;

    if (root && data.wrapperClass) {
      root.className = data.wrapperClass;
    }
    if (titleEl && data.heading) titleEl.textContent = data.heading;
    if (data.documentTitle) document.title = data.documentTitle;
    if (subEl) {
      if (data.subLine) {
        subEl.textContent = data.subLine;
        subEl.removeAttribute("hidden");
      } else {
        subEl.setAttribute("hidden", "hidden");
      }
    }
    if (leadEl) {
      if (data.leadHtml) {
        leadEl.innerHTML = data.leadHtml;
        leadEl.removeAttribute("hidden");
      } else {
        leadEl.setAttribute("hidden", "hidden");
      }
    }

    var items = (data.items || []).slice();
    var sortKey = data.timeSource === "reminder" ? "published" : "published";
    if (!data.enableSortTabs && data.timeSource === "reminder") {
      items.sort(function (a, b) {
        return Date.parse(a.published) - Date.parse(b.published);
      });
    } else {
      items.sort(function (a, b) {
        return Date.parse(b.published) - Date.parse(a.published);
      });
    }

    var shell = "";
    if (data.enableSortTabs) {
      shell += '<div class="timeline-shell" data-timeline-sort="published">';
      shell +=
        '<div class="timeline-tabs" role="tablist" aria-label="时间轴排序">';
      shell +=
        '<button type="button" class="timeline-tab is-active" role="tab" data-sort="published" aria-selected="true">入站时间</button>';
      shell +=
        '<button type="button" class="timeline-tab" role="tab" data-sort="modified" aria-selected="false">更新时间</button>';
      shell += "</div>";
    }
    shell += '<ol class="timeline timeline-page">';
    shell += renderItems(items, pagePath, sortKey);
    shell += "</ol>";
    if (data.enableSortTabs) {
      shell += "</div>";
    }
    host.outerHTML = shell;

    if (data.enableSortTabs) {
      var tabsScript = relFromTo(pagePath, "timeline-tabs.js");
      var s = document.createElement("script");
      s.src = tabsScript;
      s.defer = true;
      document.body.appendChild(s);
    }
  }

  var manifestCache = null;

  function loadManifest(pagePath) {
    if (manifestCache) return Promise.resolve(manifestCache);
    var url = relFromTo(pagePath, "data/timeline-manifest.json");
    return fetch(url, { credentials: "same-origin" })
      .then(function (r) {
        if (!r.ok) throw new Error("manifest");
        return r.json();
      })
      .then(function (m) {
        manifestCache = m;
        return m;
      });
  }

  function boot() {
    var root = document.getElementById("timeline-page-host");
    if (!root) return;
    var pagePath = document.body.getAttribute("data-page-path") || "index.html";
    loadManifest(pagePath)
      .then(function (manifest) {
        var dataUrl = manifest[normalizeWebPath(pagePath)];
        if (!dataUrl) throw new Error("missing");
        return fetch(relFromTo(pagePath, dataUrl), { credentials: "same-origin" });
      })
      .then(function (r) {
        if (!r.ok) throw new Error("data");
        return r.json();
      })
      .then(function (data) {
        renderTimeline(data, pagePath);
      })
      .catch(function () {
        root.textContent = "无法加载时间轴数据，请重新生成站点。";
      });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
