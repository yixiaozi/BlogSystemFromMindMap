/**
 * 访问统计：本机足迹（localStorage）+ Artalk 全站 PV（可用时）。
 * - 文首显示本页访问次数
 * - 右侧栏「最近访问」
 * - 「访问历史」页完整时间线
 */
(function () {
  "use strict";

  var STORAGE_KEY = "mindmapblog-visit-stats-v1";
  var SESSION_KEY = "mindmapblog-visit-session-v1";
  var MAX_HISTORY = 320;
  var SESSION_DEDUP_MS = 4000;
  var RECENT_ASIDE = 8;
  var HISTORY_PAGE = "访问历史.html";

  var ARTALK = {
    site: "馒头的思维导图博客",
    server: "https://mantoublog.top/artalk",
    localServer: "http://127.0.0.1:23366",
  };

  function ready(fn) {
    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", fn);
    } else {
      fn();
    }
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

  function pageKeyFromPath(path) {
    return "/" + normalizeWebPath(path);
  }

  function readPageContext() {
    var body = document.body;
    var pagePath = normalizeWebPath(
      (body && body.getAttribute("data-page-path")) || "index.html"
    );
    return {
      pagePath: pagePath,
      isHistoryPage:
        webFileName(pagePath).toLowerCase() === HISTORY_PAGE.toLowerCase() ||
        !!(body && body.getAttribute("data-is-visit-history-page") === "1"),
    };
  }

  function pageTitle(pagePath) {
    var h1 =
      document.querySelector(".article-title-block h1") ||
      document.querySelector(".hero .page-title") ||
      document.querySelector("main h1");
    if (h1 && h1.textContent) {
      var t = h1.textContent.replace(/\s+/g, " ").trim();
      if (t && t !== "…") return t;
    }
    var doc = (document.title || "").replace(/\s*[·|\-–—].*$/, "").trim();
    if (doc) return doc;
    return webFileName(pagePath).replace(/\.html$/i, "") || pagePath;
  }

  function emptyStore() {
    return { version: 1, history: [], pages: {}, totalVisits: 0 };
  }

  function loadStore() {
    try {
      var raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return emptyStore();
      var data = JSON.parse(raw);
      if (!data || typeof data !== "object") return emptyStore();
      if (!Array.isArray(data.history)) data.history = [];
      if (!data.pages || typeof data.pages !== "object") data.pages = {};
      if (typeof data.totalVisits !== "number") data.totalVisits = data.history.length;
      return data;
    } catch (e) {
      return emptyStore();
    }
  }

  function saveStore(store) {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(store));
    } catch (e) {}
  }

  function shouldCountThisLoad(pagePath) {
    var now = Date.now();
    try {
      var raw = sessionStorage.getItem(SESSION_KEY);
      var map = raw ? JSON.parse(raw) : {};
      if (!map || typeof map !== "object") map = {};
      var last = map[pagePath];
      if (typeof last === "number" && now - last < SESSION_DEDUP_MS) return false;
      map[pagePath] = now;
      sessionStorage.setItem(SESSION_KEY, JSON.stringify(map));
      return true;
    } catch (e) {
      return true;
    }
  }

  function recordLocalVisit(pagePath, title) {
    if (!shouldCountThisLoad(pagePath)) {
      return { store: loadStore(), counted: false };
    }
    var store = loadStore();
    var at = new Date().toISOString();
    store.history.unshift({ path: pagePath, title: title, at: at });
    if (store.history.length > MAX_HISTORY) {
      store.history.length = MAX_HISTORY;
    }
    var page = store.pages[pagePath] || { count: 0, title: title, lastAt: at };
    page.count = (page.count || 0) + 1;
    page.title = title || page.title || pagePath;
    page.lastAt = at;
    store.pages[pagePath] = page;
    store.totalVisits = (store.totalVisits || 0) + 1;
    saveStore(store);
    return { store: store, counted: true };
  }

  function formatClock(iso) {
    if (!iso) return "";
    var d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    var now = new Date();
    var pad = function (n) {
      return n < 10 ? "0" + n : String(n);
    };
    var hm = pad(d.getHours()) + ":" + pad(d.getMinutes());
    var sameDay =
      d.getFullYear() === now.getFullYear() &&
      d.getMonth() === now.getMonth() &&
      d.getDate() === now.getDate();
    if (sameDay) return "今天 " + hm;
    var yest = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);
    var isYest =
      d.getFullYear() === yest.getFullYear() &&
      d.getMonth() === yest.getMonth() &&
      d.getDate() === yest.getDate();
    if (isYest) return "昨天 " + hm;
    if (d.getFullYear() === now.getFullYear()) {
      return d.getMonth() + 1 + "月" + d.getDate() + "日 " + hm;
    }
    return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()) + " " + hm;
  }

  function formatFull(iso) {
    if (!iso) return "";
    var d = new Date(iso);
    if (isNaN(d.getTime())) return "";
    var pad = function (n) {
      return n < 10 ? "0" + n : String(n);
    };
    return (
      d.getFullYear() +
      "-" +
      pad(d.getMonth() + 1) +
      "-" +
      pad(d.getDate()) +
      " " +
      pad(d.getHours()) +
      ":" +
      pad(d.getMinutes()) +
      ":" +
      pad(d.getSeconds())
    );
  }

  function hostAllowsArtalk(hostname) {
    if (!hostname) return false;
    var h = hostname.toLowerCase();
    if (h === "localhost" || h === "127.0.0.1" || h === "[::1]") return true;
    if (h === "mantoublog.top" || h.endsWith(".mantoublog.top")) return true;
    return false;
  }

  function isGitHubPages(hostname) {
    if (!hostname) return false;
    var h = hostname.toLowerCase();
    return h === "github.io" || h.endsWith(".github.io");
  }

  function resolveArtalkServer(hostname) {
    var h = (hostname || "").toLowerCase();
    if (h === "localhost" || h === "127.0.0.1" || h === "[::1]") {
      return ARTALK.localServer;
    }
    return ARTALK.server;
  }

  function artalkPageKey(pagePath) {
    return pageKeyFromPath(pagePath);
  }

  function fetchArtalkPv(server, pagePath, increment) {
    var key = artalkPageKey(pagePath);
    var site = encodeURIComponent(ARTALK.site);
    if (increment) {
      return fetch(server.replace(/\/$/, "") + "/api/v2/pages/pv", {
        method: "POST",
        credentials: "omit",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ page_key: key, site_name: ARTALK.site }),
      })
        .then(function (r) {
          if (!r.ok) throw new Error("pv " + r.status);
          return r.json();
        })
        .then(function (data) {
          var n = data && typeof data.pv === "number" ? data.pv : null;
          return n;
        });
    }
    var q =
      server.replace(/\/$/, "") +
      "/api/v2/stats/page_pv?site_name=" +
      site +
      "&page_keys=" +
      encodeURIComponent(key);
    return fetch(q, { credentials: "omit" })
      .then(function (r) {
        if (!r.ok) throw new Error("stats " + r.status);
        return r.json();
      })
      .then(function (data) {
        var map = data && data.data ? data.data : null;
        if (!map) return null;
        var n = map[key];
        return typeof n === "number" ? n : null;
      });
  }

  function hasArticleCommentsMount() {
    return !!document.getElementById("article-comments");
  }

  function setText(el, text) {
    if (el) el.textContent = text;
  }

  function ensurePageMeter(ctx, store) {
    var localCount =
      (store.pages[ctx.pagePath] && store.pages[ctx.pagePath].count) || 0;

    var host = document.getElementById("visit-stats-meter");
    if (!host) {
      var meta = document.querySelector(".article-meta-line");
      var heroLead = document.querySelector(".hero .page-lead");
      var hero = document.querySelector(".hero");
      if (meta) {
        host = document.createElement("span");
        host.id = "visit-stats-meter";
        host.className = "visit-stats-meter visit-stats-meter--inline";
        meta.appendChild(document.createTextNode(" · "));
        meta.appendChild(host);
      } else if (hero) {
        host = document.createElement("p");
        host.id = "visit-stats-meter";
        host.className = "visit-stats-meter visit-stats-meter--hero";
        if (heroLead && heroLead.parentNode === hero) {
          hero.insertBefore(host, heroLead.nextSibling);
        } else {
          var h1 = hero.querySelector("h1");
          if (h1 && h1.nextSibling) hero.insertBefore(host, h1.nextSibling);
          else hero.appendChild(host);
        }
      } else {
        return null;
      }
    }

    host.innerHTML =
      '<span class="visit-stat-pill" title="本机浏览器累计打开本页的次数">' +
      '<span class="visit-stat-label">本机</span>' +
      '<strong class="visit-stat-local">' +
      esc(String(localCount)) +
      "</strong>" +
      "<span class=\"visit-stat-unit\">次</span></span>" +
      '<span class="visit-stat-pill visit-stat-pill--site" hidden title="全站浏览量（Artalk）">' +
      '<span class="visit-stat-label">全站</span>' +
      '<strong class="visit-stat-site">—</strong>' +
      "<span class=\"visit-stat-unit\">次</span></span>";
    return host;
  }

  function updateSitePv(host, pv) {
    if (!host || pv == null || isNaN(pv)) return;
    var pill = host.querySelector(".visit-stat-pill--site");
    var strong = host.querySelector(".visit-stat-site");
    if (pill) {
      pill.hidden = false;
      pill.removeAttribute("hidden");
    }
    setText(strong, String(pv));
  }

  function uniqueRecent(history, limit) {
    var seen = Object.create(null);
    var out = [];
    for (var i = 0; i < history.length && out.length < limit; i++) {
      var item = history[i];
      if (!item || !item.path) continue;
      var k = String(item.path).toLowerCase();
      if (seen[k]) continue;
      seen[k] = true;
      out.push(item);
    }
    return out;
  }

  function renderAsideList(pagePath, items) {
    if (!items.length) {
      return '<p class="visit-aside-empty">还没有阅读记录，随便点开一篇文章吧。</p>';
    }
    var html = '<ul class="visit-aside-list">';
    for (var i = 0; i < items.length; i++) {
      var it = items[i];
      var href = relFromTo(pagePath, it.path);
      var active = normalizeWebPath(it.path).toLowerCase() === pagePath.toLowerCase();
      html +=
        '<li class="visit-aside-item' +
        (active ? " is-active" : "") +
        '"><a class="visit-aside-link" href="' +
        escAttr(href) +
        '"><span class="visit-aside-title">' +
        esc(it.title || it.path) +
        '</span><time class="visit-aside-time" datetime="' +
        escAttr(it.at || "") +
        '">' +
        esc(formatClock(it.at)) +
        "</time></a></li>";
    }
    html += "</ul>";
    return html;
  }

  function mountAside(ctx, store) {
    var tags = document.getElementById("layout-tags");
    if (!tags) return;

    var existing = document.getElementById("visit-stats-aside");
    var historyHref = relFromTo(ctx.pagePath, HISTORY_PAGE);
    var recent = uniqueRecent(store.history || [], RECENT_ASIDE);
    var totalPages = Object.keys(store.pages || {}).length;
    var html =
      '<section class="visit-aside-wrap" id="visit-stats-aside" aria-label="最近访问">' +
      '<h3 class="aside-module-title"><a class="visit-aside-title-link" href="' +
      escAttr(historyHref) +
      '">最近访问</a></h3>' +
      '<p class="visit-aside-lead">本机足迹 · 共 ' +
      esc(String(store.totalVisits || 0)) +
      " 次 · " +
      esc(String(totalPages)) +
      " 页</p>" +
      renderAsideList(ctx.pagePath, recent) +
      '<p class="visit-aside-more-wrap"><a class="visit-aside-more" href="' +
      escAttr(historyHref) +
      '">完整访问历史</a></p>' +
      "</section>";

    if (existing) {
      existing.outerHTML = html;
      return;
    }

    var stack = tags.querySelector(".aside-main-blocks") || tags;
    var search = stack.querySelector(".search-aside-wrap");
    var wrap = document.createElement("div");
    wrap.innerHTML = html;
    var node = wrap.firstChild;
    if (search) stack.insertBefore(node, search);
    else stack.appendChild(node);
  }

  function groupHistoryByDay(history) {
    var groups = [];
    var map = Object.create(null);
    for (var i = 0; i < history.length; i++) {
      var it = history[i];
      if (!it || !it.at) continue;
      var d = new Date(it.at);
      if (isNaN(d.getTime())) continue;
      var pad = function (n) {
        return n < 10 ? "0" + n : String(n);
      };
      var key = d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate());
      if (!map[key]) {
        map[key] = { key: key, date: d, items: [] };
        groups.push(map[key]);
      }
      map[key].items.push(it);
    }
    return groups;
  }

  function dayLabel(d) {
    var now = new Date();
    var sameDay =
      d.getFullYear() === now.getFullYear() &&
      d.getMonth() === now.getMonth() &&
      d.getDate() === now.getDate();
    if (sameDay) return "今天";
    var yest = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1);
    if (
      d.getFullYear() === yest.getFullYear() &&
      d.getMonth() === yest.getMonth() &&
      d.getDate() === yest.getDate()
    ) {
      return "昨天";
    }
    var week = ["日", "一", "二", "三", "四", "五", "六"];
    return (
      d.getFullYear() +
      "年" +
      (d.getMonth() + 1) +
      "月" +
      d.getDate() +
      "日 · 周" +
      week[d.getDay()]
    );
  }

  function renderHistoryPage(ctx, store) {
    var host = document.getElementById("visit-history-host");
    if (!host) return;

    var history = store.history || [];
    var pages = store.pages || {};
    var pageKeys = Object.keys(pages);
    pageKeys.sort(function (a, b) {
      return (pages[b].count || 0) - (pages[a].count || 0);
    });
    var top = pageKeys.slice(0, 12);

    var summary =
      '<div class="visit-history-summary">' +
      '<div class="visit-history-card"><span class="visit-history-card-label">累计访问</span><strong class="visit-history-card-value">' +
      esc(String(store.totalVisits || 0)) +
      '</strong><span class="visit-history-card-unit">次</span></div>' +
      '<div class="visit-history-card"><span class="visit-history-card-label">浏览过的页面</span><strong class="visit-history-card-value">' +
      esc(String(pageKeys.length)) +
      '</strong><span class="visit-history-card-unit">页</span></div>' +
      '<div class="visit-history-card"><span class="visit-history-card-label">最近一次</span><strong class="visit-history-card-value visit-history-card-value--sm">' +
      esc(history[0] ? formatClock(history[0].at) : "—") +
      "</strong></div></div>";

    var topHtml = "";
    if (top.length) {
      topHtml = '<section class="visit-history-top" aria-label="常读页面"><h2 class="visit-history-h2">常读页面</h2><ol class="visit-top-list">';
      for (var i = 0; i < top.length; i++) {
        var p = top[i];
        var info = pages[p];
        topHtml +=
          '<li><a href="' +
          escAttr(relFromTo(ctx.pagePath, p)) +
          '"><span class="visit-top-rank">' +
          (i + 1) +
          '</span><span class="visit-top-title">' +
          esc(info.title || p) +
          '</span><span class="visit-top-count">' +
          esc(String(info.count || 0)) +
          " 次</span></a></li>";
      }
      topHtml += "</ol></section>";
    }

    var timeline = '<section class="visit-history-timeline" aria-label="访问时间线"><h2 class="visit-history-h2">访问时间线</h2>';
    if (!history.length) {
      timeline +=
        '<p class="visit-history-empty">还没有记录。浏览任意页面后，足迹会保存在本机浏览器中。</p>';
    } else {
      var groups = groupHistoryByDay(history);
      for (var g = 0; g < groups.length; g++) {
        var group = groups[g];
        timeline +=
          '<div class="visit-day-group"><h3 class="visit-day-title">' +
          esc(dayLabel(group.date)) +
          '</h3><ul class="visit-day-list">';
        for (var j = 0; j < group.items.length; j++) {
          var it = group.items[j];
          timeline +=
            '<li class="visit-day-item"><time datetime="' +
            escAttr(it.at || "") +
            '">' +
            esc(formatFull(it.at).slice(11, 16)) +
            '</time><a href="' +
            escAttr(relFromTo(ctx.pagePath, it.path)) +
            '">' +
            esc(it.title || it.path) +
            "</a></li>";
        }
        timeline += "</ul></div>";
      }
    }
    timeline += "</section>";

    var actions =
      '<div class="visit-history-actions">' +
      '<button type="button" class="visit-history-clear" id="visit-history-clear">清除本机访问记录</button>' +
      '<p class="visit-history-note">记录仅存于当前浏览器，不会上传。全站浏览量由 Artalk 单独统计。</p></div>';

    host.innerHTML = summary + topHtml + timeline + actions;

    var clearBtn = document.getElementById("visit-history-clear");
    if (clearBtn) {
      clearBtn.addEventListener("click", function () {
        if (!window.confirm("确定清除本机全部访问记录？此操作不可恢复。")) return;
        saveStore(emptyStore());
        var fresh = loadStore();
        renderHistoryPage(ctx, fresh);
        mountAside(ctx, fresh);
        var meter = document.getElementById("visit-stats-meter");
        if (meter) ensurePageMeter(ctx, fresh);
      });
    }
  }

  function whenAsideReady(cb) {
    if (document.querySelector(".aside-main-blocks") || document.querySelector(".right-aside-stack")) {
      cb();
      return;
    }
    var tags = document.getElementById("layout-tags");
    if (!tags) {
      cb();
      return;
    }
    var tries = 0;
    var timer = setInterval(function () {
      tries++;
      if (document.querySelector(".aside-main-blocks") || tries > 40) {
        clearInterval(timer);
        cb();
      }
    }, 50);
  }

  function syncArtalkPv(ctx, meter, counted) {
    var hostname = "";
    try {
      hostname = location.hostname || "";
    } catch (e) {
      hostname = "";
    }
    if (isGitHubPages(hostname) || !hostAllowsArtalk(hostname)) return;

    var server = resolveArtalkServer(hostname);
    var articleWillInc = hasArticleCommentsMount() && !isGitHubPages(hostname);

    function apply(pv) {
      updateSitePv(meter, pv);
    }

    if (articleWillInc) {
      // 文章页由 Artalk.init 计一次；此处只读取，避免重复 +1
      var tries = 0;
      var poll = function () {
        tries++;
        fetchArtalkPv(server, ctx.pagePath, false)
          .then(apply)
          .catch(function () {});
        if (tries < 6) setTimeout(poll, 700);
      };
      setTimeout(poll, 500);

      window.addEventListener(
        "mindmapblog:artalk-ready",
        function () {
          fetchArtalkPv(server, ctx.pagePath, false).then(apply).catch(function () {});
        },
        { once: true }
      );
      return;
    }

    if (!counted) {
      fetchArtalkPv(server, ctx.pagePath, false).then(apply).catch(function () {});
      return;
    }

    fetchArtalkPv(server, ctx.pagePath, true).then(apply).catch(function () {
      fetchArtalkPv(server, ctx.pagePath, false).then(apply).catch(function () {});
    });
  }

  function ensureFooterLink(ctx) {
    var links = document.querySelector(".site-footer-links");
    if (!links) return;
    if (links.querySelector('a[href*="' + HISTORY_PAGE + '"]')) return;
    var href = relFromTo(ctx.pagePath, HISTORY_PAGE);
    var sep = document.createElement("span");
    sep.className = "gen-aside-sep";
    sep.setAttribute("aria-hidden", "true");
    sep.textContent = "·";
    var a = document.createElement("a");
    a.href = href;
    a.className = "gen-aside-main-link";
    a.title = "本机访问足迹";
    a.textContent = "访问历史";
    var word = null;
    var children = links.children;
    for (var i = 0; i < children.length; i++) {
      var el = children[i];
      if (el.tagName === "A" && /词频/.test(el.textContent || "")) {
        word = el;
        break;
      }
    }
    if (word) {
      links.insertBefore(sep, word);
      links.insertBefore(a, word);
    } else {
      links.appendChild(sep);
      links.appendChild(a);
    }
  }

  function boot() {
    var ctx = readPageContext();
    var title = pageTitle(ctx.pagePath);
    var result = recordLocalVisit(ctx.pagePath, title);
    var store = result.store;
    var meter = ensurePageMeter(ctx, store);
    ensureFooterLink(ctx);

    whenAsideReady(function () {
      mountAside(ctx, loadStore());
    });

    if (ctx.isHistoryPage) {
      renderHistoryPage(ctx, store);
    }

    syncArtalkPv(ctx, meter, result.counted);
  }

  ready(boot);
})();
