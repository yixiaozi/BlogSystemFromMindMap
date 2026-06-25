(function () {
  function parseMs(iso) {
    if (!iso) return 0;
    var t = Date.parse(iso);
    return isNaN(t) ? 0 : t;
  }

  function pad2(n) {
    return n < 10 ? "0" + n : String(n);
  }

  function formatDateLabel(d) {
    return d.getFullYear() + "年" + (d.getMonth() + 1) + "月" + d.getDate() + "日";
  }

  function formatClock(d) {
    return pad2(d.getHours()) + ":" + pad2(d.getMinutes());
  }

  function formatIsoLocal(d) {
    return (
      d.getFullYear() +
      "-" +
      pad2(d.getMonth() + 1) +
      "-" +
      pad2(d.getDate()) +
      "T" +
      pad2(d.getHours()) +
      ":" +
      pad2(d.getMinutes()) +
      ":" +
      pad2(d.getSeconds())
    );
  }

  function sortKeyToAttr(sortKey) {
    return sortKey === "published" ? "data-published" : "data-modified";
  }

  function refreshDateLabels(shell) {
    var list = shell.querySelector(".timeline");
    if (!list) return;
    var sortKey = shell.getAttribute("data-timeline-sort") || "modified";
    var attr = sortKeyToAttr(sortKey);
    var items = list.querySelectorAll(".timeline-item");
    var prevDate = null;

    items.forEach(function (item) {
      var iso = item.getAttribute(attr);
      var d = new Date(iso);
      if (isNaN(d.getTime())) return;

      var lead = item.querySelector(".timeline-lead time");
      if (lead) lead.setAttribute("datetime", formatIsoLocal(d));

      var clock = item.querySelector(".timeline-clock");
      if (clock) clock.textContent = formatClock(d);

      var dateSpan = item.querySelector(".timeline-date");
      if (!dateSpan) return;

      var same =
        prevDate &&
        prevDate.getFullYear() === d.getFullYear() &&
        prevDate.getMonth() === d.getMonth() &&
        prevDate.getDate() === d.getDate();
      prevDate = d;

      if (same) {
        dateSpan.textContent = "";
        dateSpan.classList.add("timeline-date-repeat");
        dateSpan.setAttribute("aria-hidden", "true");
      } else {
        dateSpan.textContent = formatDateLabel(d);
        dateSpan.classList.remove("timeline-date-repeat");
        dateSpan.removeAttribute("aria-hidden");
      }
    });
  }

  function sortList(shell, sortKey) {
    var list = shell.querySelector(".timeline");
    if (!list) return;
    var attr = sortKeyToAttr(sortKey);
    var items = Array.prototype.slice.call(list.querySelectorAll(".timeline-item"));
    items.sort(function (a, b) {
      return parseMs(b.getAttribute(attr)) - parseMs(a.getAttribute(attr));
    });
    var frag = document.createDocumentFragment();
    items.forEach(function (el) {
      frag.appendChild(el);
    });
    list.appendChild(frag);
    shell.setAttribute("data-timeline-sort", sortKey);
    refreshDateLabels(shell);
  }

  function setActiveTab(tabs, activeBtn) {
    tabs.forEach(function (btn) {
      var on = btn === activeBtn;
      btn.setAttribute("aria-selected", on ? "true" : "false");
      btn.classList.toggle("is-active", on);
    });
  }

  function initShell(shell) {
    var tabs = Array.prototype.slice.call(shell.querySelectorAll(".timeline-tabs [data-sort]"));
    if (!tabs.length) return;

    var storageKey = "timeline-sort:" + (location.pathname || "index");
    var saved = null;
    try {
      saved = localStorage.getItem(storageKey);
    } catch (e) {}

    var initial = saved === "published" ? "published" : "modified";
    var initialBtn = tabs.find(function (b) {
      return b.getAttribute("data-sort") === initial;
    });
    if (!initialBtn) initialBtn = tabs[0];

    tabs.forEach(function (btn) {
      btn.addEventListener("click", function () {
        var key = btn.getAttribute("data-sort");
        if (!key) return;
        setActiveTab(tabs, btn);
        sortList(shell, key);
        try {
          localStorage.setItem(storageKey, key);
        } catch (e) {}
      });
    });

    setActiveTab(tabs, initialBtn);
    sortList(shell, initialBtn.getAttribute("data-sort") || "modified");
  }

  document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll(".timeline-shell").forEach(initShell);
  });
})();
