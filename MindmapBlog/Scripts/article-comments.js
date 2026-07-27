/**
 * 文章评论（Artalk）。
 * - 已有评论：直接展示在页面上（在发表区上方）
 * - 无评论：显示「暂无评论」，折叠条上也标数量
 * - 发表区：默认折叠，样式对齐「修订与对比」
 * - 仅纯文字：关闭表情、图片上传、预览等
 * - GitHub Pages（*.github.io）禁用
 */
(function () {
  var CONFIG = {
    site: "馒头的思维导图博客",
    server: "https://mantoublog.top/artalk",
    localServer: "http://127.0.0.1:23366",
    cdnCss: "https://cdn.jsdelivr.net/npm/artalk@2.9.1/dist/Artalk.css",
    cdnJs: "https://cdn.jsdelivr.net/npm/artalk@2.9.1/dist/Artalk.js",
  };

  function ready(fn) {
    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", fn);
    } else {
      fn();
    }
  }

  function hostAllowsComments(hostname) {
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

  function loadCss(href) {
    var link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = href;
    document.head.appendChild(link);
  }

  function loadScript(src) {
    return new Promise(function (resolve, reject) {
      var s = document.createElement("script");
      s.src = src;
      s.async = true;
      s.onload = function () {
        resolve();
      };
      s.onerror = function () {
        reject(new Error("failed to load " + src));
      };
      document.head.appendChild(s);
    });
  }

  function isDark() {
    return document.documentElement.classList.contains("theme-dark");
  }

  function resolveServer(hostname) {
    var h = (hostname || "").toLowerCase();
    if (h === "localhost" || h === "127.0.0.1" || h === "[::1]") {
      return CONFIG.localServer;
    }
    return CONFIG.server;
  }

  function pageKey() {
    var path = document.body && document.body.getAttribute("data-page-path");
    if (path) return "/" + String(path).replace(/^\/+/, "");
    try {
      return location.pathname || "/";
    } catch (e) {
      return "/";
    }
  }

  function pageTitle() {
    var h1 = document.querySelector(".article-title-block h1");
    if (h1 && h1.textContent) return h1.textContent.trim();
    return document.title || "";
  }

  function countComments(section) {
    if (!section) return 0;
    // Artalk 实际节点：.atk-list-body > .atk-comment-wrap（或 .atk-comment）
    var n = section.querySelectorAll(".atk-list .atk-comment-wrap").length;
    if (n > 0) return n;
    n = section.querySelectorAll(".atk-list .atk-comment").length;
    if (n > 0) return n;
    var numEl = section.querySelector(".atk-comment-count-num");
    if (numEl) {
      var parsed = parseInt(String(numEl.textContent || "").replace(/[^\d]/g, ""), 10);
      if (!isNaN(parsed)) return parsed;
    }
    return 0;
  }

  function ensureEmptyTip(listWrap) {
    var tip = listWrap.querySelector(".article-comments-empty");
    if (!tip) {
      tip = document.createElement("p");
      tip.className = "article-comments-empty";
      tip.textContent = "暂无评论";
      listWrap.appendChild(tip);
    }
    return tip;
  }

  function syncListVisibility(section) {
    var listWrap = section.querySelector("#article-comments-list-wrap");
    var listHost = section.querySelector("#article-comments-list");
    var dockTitle = section.querySelector(".comment-dock-title");
    if (!listWrap || !listHost) return;

    var n = countComments(section);
    var countEl = section.querySelector("#article-comments-count");
    if (countEl) {
      countEl.textContent = "（" + n + "）";
    }
    if (dockTitle) {
      dockTitle.textContent = n > 0 ? "发表评论 · 已有 " + n + " 条" : "发表评论";
    }

    // 列表区始终可见，便于看出有没有评论
    listWrap.hidden = false;
    listWrap.removeAttribute("hidden");

    var tip = ensureEmptyTip(listWrap);
    if (n > 0) {
      tip.hidden = true;
      listHost.hidden = false;
    } else {
      tip.hidden = false;
      // 仍保留 list 容器，方便 Artalk 后续插入
      listHost.hidden = false;
    }
  }

  /**
   * Artalk 会把 class="artalk" 加在挂载节点本身（#Comments），
   * 因此不能用 mountEl.querySelector('.artalk')（查不到自身）。
   */
  function findArtalkList(mountEl, section) {
    return (
      (mountEl && mountEl.querySelector(".atk-list")) ||
      (section && section.querySelector(".atk-list")) ||
      null
    );
  }

  /** 把评论列表挪到发表区上方；编辑器留在折叠坞内。 */
  function rearrangeLayout(section, mountEl) {
    var list = findArtalkList(mountEl, section);
    var listHost = section.querySelector("#article-comments-list");
    if (list && listHost && list.parentElement !== listHost) {
      listHost.appendChild(list);
    }
    syncListVisibility(section);
  }

  function openCommentDock(section) {
    var dock = section.querySelector(".comment-dock");
    if (!dock) return;
    dock.open = true;
    // 等折叠动画后再聚焦
    setTimeout(function () {
      try {
        dock.scrollIntoView({ behavior: "smooth", block: "nearest" });
      } catch (e) {}
      var ta = section.querySelector("#Comments textarea.atk-textarea, #Comments .atk-textarea");
      if (ta) {
        try {
          ta.focus();
        } catch (e2) {}
      }
    }, 80);
  }

  function wireReplyOpensDock(section) {
    section.addEventListener(
      "click",
      function (ev) {
        var el = ev.target;
        if (!el || !el.closest) return;
        if (!el.closest(".atk-comment, .atk-list")) return;

        var btn = el.closest(
          ".atk-reply, .atk-comment-reply, [data-action=\"reply\"], .atk-comment-actions > span, .atk-actions > span, .atk-actions > a, .atk-actions > button"
        );
        if (!btn) return;

        var label = (btn.getAttribute("title") || btn.getAttribute("aria-label") || btn.textContent || "")
          .replace(/\s+/g, " ")
          .trim();
        // Artalk 回复按钮文案一般为「回复」；排除点赞等
        if (label.indexOf("回复") === -1 && !btn.classList.contains("atk-reply")) return;

        openCommentDock(section);
      },
      true
    );
  }

  ready(function () {
    var section = document.getElementById("article-comments");
    var host = document.getElementById("Comments");
    if (!section || !host) return;

    var hostname = "";
    try {
      hostname = location.hostname || "";
    } catch (e) {
      hostname = "";
    }

    if (isGitHubPages(hostname) || !hostAllowsComments(hostname)) {
      section.remove();
      return;
    }

        section.hidden = false;
    // 先露出列表区骨架，避免“完全看不出有没有评论”
    var listWrapEarly = section.querySelector("#article-comments-list-wrap");
    if (listWrapEarly) {
      listWrapEarly.hidden = false;
      listWrapEarly.removeAttribute("hidden");
      ensureEmptyTip(listWrapEarly).textContent = "加载评论…";
    }

    wireReplyOpensDock(section);

    loadCss(CONFIG.cdnCss);
    loadScript(CONFIG.cdnJs)
      .then(function () {
        if (typeof Artalk === "undefined" || !Artalk.init) {
          throw new Error("Artalk global missing");
        }

        var artalk = Artalk.init({
          el: host,
          pageKey: pageKey(),
          pageTitle: pageTitle(),
          server: resolveServer(hostname),
          site: CONFIG.site,
          darkMode: isDark(),
          locale: "zh-CN",
          preferRemoteConf: false,
          emoticons: false,
          imgUpload: false,
          preview: false,
          vote: false,
          voteDown: false,
          pageVote: false,
          uaBadge: false,
          listSort: false,
          editorTravel: false,
          // 嵌套显示：回复挂在原评论下方（不要平铺）
          flatMode: false,
          nestMax: 3,
          nestSort: "DATE_ASC",
          noComment: "暂无评论",
          placeholder: "写下你的想法（仅文字）…",
          sendBtn: "发送",
        });

        if (artalk && typeof artalk.update === "function") {
          artalk.update({
            emoticons: false,
            imgUpload: false,
            preview: false,
            vote: false,
            listSort: false,
            flatMode: false,
            nestMax: 3,
            nestSort: "DATE_ASC",
            placeholder: "写下你的想法（仅文字）…",
            sendBtn: "发送",
            noComment: "暂无评论",
          });
        }

        setTimeout(function () {
          var ta = host.querySelector("textarea.atk-textarea, .atk-editor textarea");
          if (ta) ta.setAttribute("placeholder", "写下你的想法（仅文字）…");
          var btn = host.querySelector(".atk-send-btn, button.atk-send");
          if (btn) btn.textContent = "发送";
        }, 50);

        function afterListChange() {
          rearrangeLayout(section, host);
        }

        if (artalk && typeof artalk.on === "function") {
          artalk.on("list-loaded", afterListChange);
          artalk.on("list-fetched", afterListChange);
          artalk.on("conf-loaded", function () {
            artalk.update({ flatMode: false, nestMax: 3, nestSort: "DATE_ASC" });
          });
        }

        // 仅延迟补一次布局，禁止轮询/MutationObserver（会卡死页面）
        setTimeout(afterListChange, 500);

        var darkBtn = document.getElementById("site-theme-dark-toggle");
        if (darkBtn && artalk && typeof artalk.setDarkMode === "function") {
          darkBtn.addEventListener("click", function () {
            setTimeout(function () {
              artalk.setDarkMode(isDark());
            }, 0);
          });
        }
      })
      .catch(function (err) {
        console.warn("[article-comments]", err);
        var tip = document.createElement("p");
        tip.className = "article-comments-error";
        tip.textContent = "评论暂时不可用（请确认 Artalk 服务已部署，且本机用 http 打开站点）。";
        host.appendChild(tip);
        var dock = section.querySelector(".comment-dock");
        if (dock) dock.open = true;
        if (listWrapEarly) {
          var empty = ensureEmptyTip(listWrapEarly);
          empty.hidden = false;
          empty.textContent = "评论加载失败";
        }
      });
  });
})();
