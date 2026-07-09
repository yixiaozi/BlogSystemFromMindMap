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

  function tagCloudFontRem(count, minCount, maxCount) {
    var lo = 0.72;
    var hi = 1.14;
    if (maxCount <= minCount) return (lo + hi) / 2;
    var logMin = Math.log(Math.max(1, minCount));
    var logMax = Math.log(Math.max(1, maxCount));
    var logC = Math.log(Math.max(1, count));
    var t = logMax <= logMin ? 1 : (logC - logMin) / (logMax - logMin);
    if (t < 0) t = 0;
    if (t > 1) t = 1;
    return lo + t * (hi - lo);
  }

  function readPageContext() {
    var body = document.body;
    return {
      pagePath: body.getAttribute("data-page-path") || "index.html",
      activeArticle: body.getAttribute("data-active-article") || "",
      highlightTag: body.getAttribute("data-highlight-tag") || "",
      isSearchPage: body.getAttribute("data-is-search-page") === "1",
    };
  }

  function renderArticlesList(articles, pagePath, activeArticle) {
    if (!articles || !articles.length) return "";
    var html = '<ul class="nav-articles">';
    for (var i = 0; i < articles.length; i++) {
      var a = articles[i];
      var href = relFromTo(pagePath, a.href);
      var active =
        activeArticle &&
        a.href &&
        activeArticle.toLowerCase() === a.href.toLowerCase();
      html +=
        '<li' +
        (active ? ' class="is-active"' : "") +
        '><a href="' +
        escAttr(href) +
        '">' +
        esc(a.title) +
        "</a></li>";
    }
    html += "</ul>";
    return html;
  }

  function renderMapTrie(node, pagePath, activeArticle) {
    var html = "";
    var segs = node.segments || [];
    for (var i = 0; i < segs.length; i++) {
      var s = segs[i];
      var href = relFromTo(pagePath, s.listPage);
      html +=
        '<details class="nav-mmnod" id="' +
        escAttr(s.detailsId) +
        '"><summary class="nav-mmnod-summary"><a class="nav-branch-title" href="' +
        escAttr(href) +
        '" onclick="event.stopPropagation()">' +
        esc(s.name) +
        '</a></summary><div class="nav-mmnod-body">';
      html += renderMapTrie(s.node, pagePath, activeArticle);
      html += "</div></details>";
    }
    html += renderArticlesList(node.articles, pagePath, activeArticle);
    return html;
  }

  function renderFolderNode(node, pagePath, activeArticle) {
    var html = "";
    var dirs = node.dirs || [];
    for (var i = 0; i < dirs.length; i++) {
      var d = dirs[i];
      var href = relFromTo(pagePath, d.listPage);
      html +=
        '<details class="nav-folder" id="' +
        escAttr(d.detailsId) +
        '"><summary class="nav-folder-summary"><a class="nav-branch-title" href="' +
        escAttr(href) +
        '" onclick="event.stopPropagation()">' +
        esc(d.name) +
        '</a></summary><div class="nav-folder-body">';
      html += renderFolderNode(d.children, pagePath, activeArticle);
      html += "</div></details>";
    }
    var mms = node.mindmaps || [];
    for (var j = 0; j < mms.length; j++) {
      var m = mms[j];
      var mmHref = relFromTo(pagePath, m.listPage);
      html +=
        '<details class="nav-mmfile" id="' +
        escAttr(m.detailsId) +
        '"><summary class="nav-mmfile-summary"><a class="nav-branch-title" href="' +
        escAttr(mmHref) +
        '" onclick="event.stopPropagation()">' +
        esc(m.label) +
        '</a></summary><div class="nav-mmfile-body">';
      html += renderMapTrie(m.root, pagePath, activeArticle);
      html += "</div></details>";
    }
    return html;
  }

  function renderMonthGrid(cal, year, month, pagePath) {
    var dayCounts = cal.dayCounts || {};
    var dayPages = cal.dayPages || {};
    var first = new Date(year, month - 1, 1);
    var start = new Date(first);
    start.setDate(start.getDate() - start.getDay());
    var weekLabels = ["日", "一", "二", "三", "四", "五", "六"];
    var html =
      '<table class="nav-cal-grid" aria-label="按日期查看任务"><thead><tr>';
    for (var w = 0; w < weekLabels.length; w++) {
      html += "<th scope=\"col\">" + weekLabels[w] + "</th>";
    }
    html += "</tr></thead><tbody>";
    for (var row = 0; row < 6; row++) {
      html += "<tr>";
      for (var col = 0; col < 7; col++) {
        var dt = new Date(start);
        dt.setDate(start.getDate() + row * 7 + col);
        var inMonth = dt.getMonth() === month - 1;
        var key =
          dt.getFullYear() +
          "-" +
          String(dt.getMonth() + 1).padStart(2, "0") +
          "-" +
          String(dt.getDate()).padStart(2, "0");
        var count = dayCounts[key] || 0;
        html += '<td class="' + (inMonth ? "in" : "out") + '">';
        if (count > 0 && dayPages[key]) {
          var dayHref = relFromTo(pagePath, dayPages[key]);
          html +=
            '<a class="nav-cal-day has-task" href="' +
            escAttr(dayHref) +
            '"><span class="d">' +
            dt.getDate() +
            '</span><span class="n">' +
            count +
            "项</span></a>";
        } else {
          html +=
            '<span class="nav-cal-day"><span class="d">' +
            dt.getDate() +
            '</span><span class="n">0</span></span>';
        }
        html += "</td>";
      }
      html += "</tr>";
    }
    html += "</tbody></table>";
    return html;
  }

  function renderCalendarVisual(cal, pagePath) {
    if (!cal || !cal.dayCounts) return "";
    var ymList = cal.yearMonths || [];
    var yDefault = cal.defaultYear;
    var mDefault = cal.defaultMonth;
    var html = '<div class="nav-cal-visual" data-default-mode="month">';
    html += '<div class="nav-cal-visual-head"><p class="nav-cal-visual-title">日历视图</p>';
    html +=
      '<div class="nav-cal-mode-switch" role="tablist" aria-label="计划视图切换">';
    html +=
      '<button type="button" class="nav-cal-mode-btn is-active" data-mode="month">月视图</button>';
    html +=
      '<button type="button" class="nav-cal-mode-btn" data-mode="year">年视图</button></div></div>';

    html += '<div class="nav-cal-controls" data-mode-panel="month">';
    html += '<label class="nav-cal-select-label" for="nav-cal-year-sel">年份</label>';
    html += '<select id="nav-cal-year-sel" class="nav-cal-select">';
    var years = {};
    for (var i = 0; i < ymList.length; i++) {
      var ym = ymList[i];
      var y = parseInt(ym.split("-")[0], 10);
      if (!years[y]) {
        years[y] = true;
        html +=
          '<option value="' +
          y +
          '"' +
          (y === yDefault ? " selected" : "") +
          ">" +
          y +
          "年</option>";
      }
    }
    html += "</select>";
    html +=
      '<label class="nav-cal-select-label" for="nav-cal-month-sel">月份</label>';
    html += '<div class="nav-cal-month-stepper">';
    html +=
      '<button type="button" class="nav-cal-step-btn" id="nav-cal-prev-month" aria-label="上一有计划的月份">‹</button>';
    html += '<select id="nav-cal-month-sel" class="nav-cal-select">';
    for (var j = 0; j < ymList.length; j++) {
      var ymKey = ymList[j];
      var parts = ymKey.split("-");
      var yy = parseInt(parts[0], 10);
      var mm = parseInt(parts[1], 10);
      html +=
        '<option value="' +
        escAttr(ymKey) +
        '" data-year="' +
        yy +
        '"' +
        (yy === yDefault && mm === mDefault ? " selected" : "") +
        ">" +
        mm +
        "月</option>";
    }
    html +=
      '</select><button type="button" class="nav-cal-step-btn" id="nav-cal-next-month" aria-label="下一有计划的月份">›</button></div></div>';

    html += '<div class="nav-cal-visual-panels">';
    for (var k = 0; k < ymList.length; k++) {
      var ymK = ymList[k];
      var p = ymK.split("-");
      var yK = parseInt(p[0], 10);
      var mK = parseInt(p[1], 10);
      var isActive = yK === yDefault && mK === mDefault;
      html +=
        '<div class="nav-cal-month-panel' +
        (isActive ? " is-active" : "") +
        '" data-ym="' +
        escAttr(ymK) +
        '" data-mode="month"' +
        (isActive ? "" : " hidden") +
        ">";
      html += "<p class=\"nav-cal-panel-title\">" + mK + "月</p>";
      html += renderMonthGrid(cal, yK, mK, pagePath);
      html += "</div>";
    }

    var monthPages = cal.monthPages || {};
    var dayCounts = cal.dayCounts || {};
    var yearKeys = Object.keys(years).map(Number).sort(function (a, b) {
      return a - b;
    });
    for (var yi = 0; yi < yearKeys.length; yi++) {
      var yv = yearKeys[yi];
      var yActive = yv === yDefault;
      html +=
        '<div class="nav-cal-year-panel' +
        (yActive ? " is-active" : "") +
        '" data-year="' +
        yv +
        '" data-mode="year" hidden>';
      html += "<p class=\"nav-cal-panel-title\">" + yv + "年计划总览</p>";
      html += '<div class="nav-cal-year-grid">';
      for (var m = 1; m <= 12; m++) {
        var ymKey = yv + "-" + String(m).padStart(2, "0");
        var monthCount = 0;
        var dcKeys = Object.keys(dayCounts);
        for (var di = 0; di < dcKeys.length; di++) {
          var dk = dcKeys[di].split("-");
          if (parseInt(dk[0], 10) === yv && parseInt(dk[1], 10) === m) {
            monthCount += dayCounts[dcKeys[di]];
          }
        }
        if (monthCount > 0 && monthPages[ymKey]) {
          var mHref = relFromTo(pagePath, monthPages[ymKey]);
          html +=
            '<a class="nav-cal-month-card has-task" href="' +
            escAttr(mHref) +
            '"><span class="m">' +
            m +
            '月</span><span class="c">' +
            monthCount +
            "项</span></a>";
        } else {
          html +=
            '<span class="nav-cal-month-card"><span class="m">' +
            m +
            '月</span><span class="c">0项</span></span>';
        }
      }
      html += "</div></div>";
    }
    html += "</div></div>";
    return html;
  }

  function renderCalendarTree(cal, pagePath, activeArticle) {
    if (!cal || !cal.tree || !cal.tree.length) return "";
    var html =
      '<section class="nav-cal-section" aria-label="计划日期"><details class="nav-cal-fold" open>';
    html +=
      '<summary class="nav-cal-fold-summary"><h3 class="aside-module-title">计划日期（提醒）</h3></summary>';
    html += '<div class="nav-cal-fold-body"><div class="nav-cal-root">';
    for (var yi = 0; yi < cal.tree.length; yi++) {
      var y = cal.tree[yi];
      var yHref = relFromTo(pagePath, y.listPage);
      html +=
        '<details class="nav-cal nav-cal-year" id="' +
        escAttr(y.detailsId) +
        '"><summary class="nav-cal-summary nav-folder-summary"><a class="nav-branch-title" href="' +
        escAttr(yHref) +
        '" onclick="event.stopPropagation()">' +
        y.year +
        '年</a></summary><div class="nav-cal-body">';
      for (var mi = 0; mi < (y.months || []).length; mi++) {
        var mo = y.months[mi];
        var mHref = relFromTo(pagePath, mo.listPage);
        html +=
          '<details class="nav-cal nav-cal-month" id="' +
          escAttr(mo.detailsId) +
          '"><summary class="nav-cal-summary nav-folder-summary"><a class="nav-branch-title" href="' +
          escAttr(mHref) +
          '" onclick="event.stopPropagation()">' +
          mo.month +
          '月</a></summary><div class="nav-cal-body">';
        for (var di = 0; di < (mo.days || []).length; di++) {
          var day = mo.days[di];
          var dHref = relFromTo(pagePath, day.listPage);
          html +=
            '<details class="nav-cal nav-cal-day" id="' +
            escAttr(day.detailsId) +
            '"><summary class="nav-cal-summary nav-folder-summary"><a class="nav-branch-title" href="' +
            escAttr(dHref) +
            '" onclick="event.stopPropagation()">' +
            day.day +
            '日</a></summary><div class="nav-cal-body"><ul class="nav-articles nav-cal-articles">';
          for (var ai = 0; ai < (day.articles || []).length; ai++) {
            var art = day.articles[ai];
            var artHref = relFromTo(pagePath, art.href);
            var active =
              activeArticle &&
              art.href &&
              activeArticle.toLowerCase() === art.href.toLowerCase();
            html +=
              "<li" +
              (active ? ' class="is-active"' : "") +
              '><span class="nav-cal-time">' +
              esc(art.time) +
              '</span><a href="' +
              escAttr(artHref) +
              '">' +
              esc(art.title) +
              "</a></li>";
          }
          html += "</ul></div></details>";
        }
        html += "</div></details>";
      }
      html += "</div></details>";
    }
    html += "</div>";
    html += renderCalendarVisual(cal, pagePath);
    html += "</div></details></section>";
    return html;
  }

  function initCalendarVisual(root) {
    if (!root) return;
    var modeBtns = root.querySelectorAll(".nav-cal-mode-btn");
    var monthPanels = root.querySelectorAll(".nav-cal-month-panel");
    var yearPanels = root.querySelectorAll(".nav-cal-year-panel");
    var yearSel = root.querySelector("#nav-cal-year-sel");
    var monthSel = root.querySelector("#nav-cal-month-sel");
    var prevBtn = root.querySelector("#nav-cal-prev-month");
    var nextBtn = root.querySelector("#nav-cal-next-month");
    var mode = root.getAttribute("data-default-mode") || "month";

    function visibleMonthOptions() {
      if (!monthSel || !yearSel) return [];
      var y = yearSel.value;
      return Array.prototype.filter.call(monthSel.options, function (o) {
        return o.getAttribute("data-year") === y;
      });
    }

    function showMonth(ym) {
      monthPanels.forEach(function (p) {
        var on = p.getAttribute("data-ym") === ym;
        p.classList.toggle("is-active", on);
        if (mode === "month") p.hidden = !on;
      });
    }

    function showYear(y) {
      yearPanels.forEach(function (p) {
        var on = p.getAttribute("data-year") === y;
        p.classList.toggle("is-active", on);
        if (mode === "year") p.hidden = !on;
      });
    }

    function syncMonthOptionsByYear() {
      if (!monthSel || !yearSel) return;
      var opts = visibleMonthOptions();
      var cur = monthSel.value;
      var ok = opts.some(function (o) {
        return o.value === cur;
      });
      if (!ok && opts.length) monthSel.value = opts[0].value;
    }

    function stepMonth(delta) {
      var opts = visibleMonthOptions();
      if (!monthSel || !opts.length) return;
      var cur = monthSel.options[monthSel.selectedIndex];
      var idx = opts.indexOf(cur);
      if (idx < 0) {
        monthSel.value = opts[0].value;
        showMonth(monthSel.value);
        return;
      }
      var j = idx + delta;
      if (j >= 0 && j < opts.length) {
        monthSel.value = opts[j].value;
        showMonth(monthSel.value);
        return;
      }
      if (!yearSel) return;
      var years = Array.prototype.slice.call(yearSel.options);
      var yi = years.indexOf(yearSel.options[yearSel.selectedIndex]);
      if (delta < 0 && yi > 0) {
        yearSel.selectedIndex = yi - 1;
        syncMonthOptionsByYear();
        var tail = visibleMonthOptions();
        if (tail.length) {
          monthSel.value = tail[tail.length - 1].value;
          showMonth(monthSel.value);
        }
        showYear(yearSel.value);
      } else if (delta > 0 && yi < years.length - 1) {
        yearSel.selectedIndex = yi + 1;
        syncMonthOptionsByYear();
        var head = visibleMonthOptions();
        if (head.length) {
          monthSel.value = head[0].value;
          showMonth(monthSel.value);
        }
        showYear(yearSel.value);
      }
    }

    function applyMode(nextMode) {
      mode = nextMode;
      modeBtns.forEach(function (b) {
        var active = b.getAttribute("data-mode") === mode;
        b.classList.toggle("is-active", active);
      });
      var controls = root.querySelector(".nav-cal-controls");
      if (controls) controls.hidden = mode !== "month";
      monthPanels.forEach(function (p) {
        p.hidden = mode !== "month" || !p.classList.contains("is-active");
      });
      yearPanels.forEach(function (p) {
        p.hidden = mode !== "year" || !p.classList.contains("is-active");
      });
    }

    if (yearSel) {
      yearSel.addEventListener("change", function () {
        syncMonthOptionsByYear();
        if (mode === "month" && monthSel) showMonth(monthSel.value);
        showYear(yearSel.value);
      });
    }
    if (monthSel) {
      monthSel.addEventListener("change", function () {
        showMonth(monthSel.value);
      });
    }
    if (prevBtn) prevBtn.addEventListener("click", function () {
      stepMonth(-1);
    });
    if (nextBtn) nextBtn.addEventListener("click", function () {
      stepMonth(1);
    });
    modeBtns.forEach(function (btn) {
      btn.addEventListener("click", function () {
        applyMode(btn.getAttribute("data-mode") || "month");
      });
    });
    syncMonthOptionsByYear();
    if (monthSel) showMonth(monthSel.value);
    if (yearSel) showYear(yearSel.value);
    applyMode(mode);
  }

  function initCalFold() {
    var fold = document.querySelector(".nav-cal-fold");
    if (!fold) return;
    try {
      if (window.matchMedia && window.matchMedia("(max-width: 960px)").matches) {
        fold.open = false;
      } else {
        fold.open = true;
      }
    } catch (e) {}
  }

  function renderNav(nav, ctx) {
    var html = '<nav class="nav-tree">';
    html +=
      '<section class="nav-filetree-section" aria-label="文件导图节点">';
    html += '<h3 class="aside-module-title">文件导图节点</h3>';
    html += '<div class="nav-root">';
    html += renderFolderNode(nav.folderTree, ctx.pagePath, ctx.activeArticle);
    html += "</div></section>";
    if (nav.calendar) {
      html += '<hr class="nav-major-divider" />';
      html += renderCalendarTree(nav.calendar, ctx.pagePath, ctx.activeArticle);
    }
    html += "</nav>";
    return html;
  }

  function renderAside(aside, ctx) {
    var html = '<div class="right-aside-stack"><div class="aside-main-blocks">';
    var p = aside.profile || {};
    if (p.aboutPage) {
      var aboutHref = relFromTo(ctx.pagePath, p.aboutPage);
      html += '<section class="aside-profile-wrap" aria-label="站长">';
      html +=
        '<a class="aside-profile-card" href="' +
        escAttr(aboutHref) +
        '"><span class="aside-profile-visual">';
      if (p.avatar) {
        var avSrc = relFromTo(ctx.pagePath, p.avatar);
        html +=
          '<img class="aside-profile-avatar" src="' +
          escAttr(avSrc) +
          '" alt="" width="80" height="80" decoding="async"/>';
      } else {
        html +=
          '<span class="aside-profile-placeholder" aria-hidden="true"></span>';
      }
      html +=
        '</span><span class="aside-profile-quote">' +
        esc(p.signature) +
        '</span><span class="aside-profile-cta">关于我</span></a></section>';
    }

    var tags = aside.tags || [];
    html += '<div class="tag-aside-inner"><h3 class="aside-module-title">书签</h3>';
    if (!tags.length) {
      html += '<p class="tag-aside-empty">暂无书签</p></div>';
    } else {
      var minC = tags[0].count;
      var maxC = tags[0].count;
      for (var i = 1; i < tags.length; i++) {
        if (tags[i].count < minC) minC = tags[i].count;
        if (tags[i].count > maxC) maxC = tags[i].count;
      }
      html += '<div class="tag-cloud" role="navigation" aria-label="书签词云">';
      for (var j = 0; j < tags.length; j++) {
        var t = tags[j];
        var tagHref = relFromTo(ctx.pagePath, t.page);
        var active =
          ctx.highlightTag &&
          t.name &&
          ctx.highlightTag.toLowerCase() === t.name.toLowerCase();
        var rem = tagCloudFontRem(t.count, minC, maxC).toFixed(3);
        html +=
          '<a href="' +
          escAttr(tagHref) +
          '" class="tag-cloud-link' +
          (active ? " is-active" : "") +
          '" style="font-size:' +
          rem +
          'rem" title="' +
          escAttr(t.count + " 篇：" + t.name) +
          '">' +
          esc(t.name) +
          "</a>";
      }
      html += "</div></div>";
    }

    var g = aside.gallery || {};
    html += '<div class="gallery-aside-inner"><h3 class="aside-module-title">图册</h3>';
    html +=
      "<p class=\"gallery-aside-lead\">正文里的图片 · 点此进入对应文章位置</p>";
    var galleryHref = relFromTo(ctx.pagePath, g.page || "图册.html");
    if (!g.total) {
      html +=
        "<p class=\"gallery-aside-hint\">导图文章正文内插入图片并重新生成后，缩略图会出现在这里；点击图片将打开该文章并定位到图中。</p>";
      html +=
        '<p class="gallery-aside-more-wrap"><a href="' +
        escAttr(galleryHref) +
        '" class="gallery-aside-more">图册索引</a></p></div>';
    } else {
      html += '<div class="gallery-aside-preview" aria-label="文章配图预览">';
      var preview = g.preview || [];
      for (var k = 0; k < preview.length; k++) {
        var e = preview[k];
        var src = relFromTo(ctx.pagePath, e.media);
        var articleHref = relFromTo(ctx.pagePath, e.article);
        var jump = articleHref + "#img-" + e.imageIndex;
        html +=
          '<a class="gallery-aside-thumb" href="' +
          escAttr(jump) +
          '" title="' +
          escAttr(e.caption) +
          '"><img src="' +
          escAttr(src) +
          '" alt="' +
          escAttr(e.caption) +
          '" loading="lazy" /></a>';
      }
      html += "</div>";
      html +=
        '<p class="gallery-aside-more-wrap"><a href="' +
        escAttr(galleryHref) +
        '" class="gallery-aside-more">图册索引 · ' +
        g.total +
        " 张</a></p></div>";
    }

    if (!ctx.isSearchPage) {
      var s = aside.search || {};
      var indexHref = relFromTo(ctx.pagePath, s.index || "data/search-index.json");
      var searchPageHref = relFromTo(ctx.pagePath, s.page || "搜索.html");
      var scriptHref = relFromTo(ctx.pagePath, "search-aside.js");
      html +=
        '<section class="search-aside-wrap" id="site-search-aside" aria-label="全文搜索" data-index-href="' +
        escAttr(indexHref) +
        '" data-page-path="' +
        escAttr(ctx.pagePath) +
        '">';
      html +=
        '<h3 class="aside-module-title"><a class="search-aside-title-link" href="' +
        escAttr(searchPageHref) +
        '">搜索</a></h3>';
      html +=
        "<p class=\"search-aside-lead\">标题 · 正文 · 书签 · 配图说明 · 分区 · 导图文件名</p>";
      html +=
        '<label class="search-aside-label visually-hidden" for="site-search-q">搜索文章</label>';
      html +=
        '<input type="search" id="site-search-q" class="search-aside-input" autocomplete="off" placeholder="输入关键词…" />';
      html +=
        '<p class="search-aside-status" id="site-search-status" aria-live="polite"></p>';
      html +=
        '<ul class="search-aside-list" id="site-search-list" hidden></ul></section>';
    }

    html += "</div></div>";
    return html;
  }

  function loadSearchScript() {
    var existing = document.querySelector('script[src*="search-aside.js"]');
    if (existing) return;
    var ctx = readPageContext();
    var script = document.createElement("script");
    script.src = relFromTo(ctx.pagePath, "search-aside.js");
    script.defer = true;
    document.body.appendChild(script);
  }

  function boot() {
    var navHost = document.getElementById("site-chrome-nav-host");
    var asideHost = document.getElementById("site-chrome-aside-host");
    if (!navHost && !asideHost) return;

    var ctx = readPageContext();
    var navUrl = relFromTo(ctx.pagePath, "data/site-nav.json");
    var asideUrl = relFromTo(ctx.pagePath, "data/site-aside.json");

    var navP = fetch(navUrl, { credentials: "same-origin" })
      .then(function (r) {
        if (!r.ok) throw new Error("nav");
        return r.json();
      })
      .catch(function () {
        return null;
      });

    var asideP = fetch(asideUrl, { credentials: "same-origin" })
      .then(function (r) {
        if (!r.ok) throw new Error("aside");
        return r.json();
      })
      .catch(function () {
        return null;
      });

    Promise.all([navP, asideP]).then(function (res) {
      var nav = res[0];
      var aside = res[1];
      if (navHost) {
        if (nav) {
          navHost.outerHTML = renderNav(nav, ctx);
          initCalFold();
          var visual = document.querySelector(".nav-cal-visual");
          if (visual) initCalendarVisual(visual);
          if (typeof window.MindmapBlogInitNav === "function") {
            window.MindmapBlogInitNav();
          }
        } else {
          navHost.textContent = "无法加载目录数据，请重新生成站点。";
        }
      }
      if (asideHost) {
        if (aside) {
          asideHost.outerHTML = renderAside(aside, ctx);
          if (!ctx.isSearchPage) loadSearchScript();
        } else {
          asideHost.textContent = "无法加载侧栏数据，请重新生成站点。";
        }
      }
    });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
