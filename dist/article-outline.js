(function () {
  function ready(fn) {
    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", fn);
    } else {
      fn();
    }
  }

  ready(function () {
    var article = document.querySelector("article.content");
    var dock = document.getElementById("article-outline-dock");
    if (!article || !dock) return;

    var blocks = Array.prototype.slice.call(article.querySelectorAll(".mm-block"));
    if (blocks.length < 2) {
      dock.remove();
      return;
    }

    function depthOf(el) {
      var m = (el.className || "").match(/mm-depth-(\d+)/);
      return m ? parseInt(m[1], 10) : 1;
    }

    function truncate(text, max) {
      text = (text || "").replace(/\s+/g, " ").trim();
      return text.length > max ? text.slice(0, max) + "…" : text;
    }

    function buildTree(elements) {
      var root = { depth: 0, children: [] };
      var stack = [root];
      elements.forEach(function (el) {
        var depth = depthOf(el);
        var node = { el: el, depth: depth, children: [] };
        while (stack.length > 1 && stack[stack.length - 1].depth >= depth) stack.pop();
        stack[stack.length - 1].children.push(node);
        stack.push(node);
      });
      return root;
    }

    function hasNested(nodes) {
      for (var i = 0; i < nodes.length; i++) {
        if (nodes[i].children.length) return true;
      }
      return false;
    }

    function defaultOpen(depth) {
      return depth <= 2;
    }

    blocks.forEach(function (el, i) {
      if (!el.id) el.id = "mm-block-" + i;
    });

    var tree = buildTree(blocks);
    if (!hasNested(tree.children)) {
      dock.remove();
      return;
    }

    dock.hidden = false;
    var links = [];

    function renderBody(container, nodes) {
      nodes.forEach(function (node) {
        if (node.children.length) {
          var details = document.createElement("details");
          details.className = "mm-fold mm-fold-d" + node.depth;
          if (defaultOpen(node.depth)) details.open = true;
          var summary = document.createElement("summary");
          summary.appendChild(node.el);
          details.appendChild(summary);
          var body = document.createElement("div");
          body.className = "mm-fold-body";
          renderBody(body, node.children);
          details.appendChild(body);
          container.appendChild(details);
        } else {
          container.appendChild(node.el);
        }
      });
    }

    var bodyFrag = document.createDocumentFragment();
    renderBody(bodyFrag, tree.children);
    article.appendChild(bodyFrag);

    function renderOutlineNav(container, nodes) {
      var ul = document.createElement("ul");
      nodes.forEach(function (node) {
        var li = document.createElement("li");
        var el = node.el;
        var btn = document.createElement("button");
        btn.type = "button";
        btn.className = "outline-jump";
        btn.dataset.depth = String(node.depth);
        btn.dataset.target = el.id;
        btn.textContent = truncate(el.textContent, 24);
        btn.title = el.textContent.trim();
        btn.addEventListener("click", function (e) {
          e.preventDefault();
          e.stopPropagation();
          el.scrollIntoView({ behavior: "smooth", block: "start" });
        });
        links.push({ btn: btn, el: el });

        if (node.children.length) {
          var details = document.createElement("details");
          details.className = "outline-node has-children";
          if (defaultOpen(node.depth)) details.open = true;
          var summary = document.createElement("summary");
          summary.appendChild(btn);
          details.appendChild(summary);
          var childWrap = document.createDocumentFragment();
          renderOutlineNav(childWrap, node.children);
          if (childWrap.firstChild) details.appendChild(childWrap.firstChild);
          li.appendChild(details);
        } else {
          var leaf = document.createElement("details");
          leaf.className = "outline-node";
          leaf.open = true;
          var leafSummary = document.createElement("summary");
          leafSummary.appendChild(btn);
          leaf.appendChild(leafSummary);
          li.appendChild(leaf);
        }
        ul.appendChild(li);
      });
      container.appendChild(ul);
      return container;
    }

    var topLevel = tree.children.length;
    var rootDetails = document.createElement("details");
    rootDetails.className = "article-outline-root";
    rootDetails.open = true;
    var rootSummary = document.createElement("summary");
    rootSummary.innerHTML =
      "层级结构 <span class=\"outline-meta\">" +
      topLevel +
      " 个一级 · " +
      blocks.length +
      " 节点</span>";
    rootDetails.appendChild(rootSummary);
    var scroll = document.createElement("div");
    scroll.className = "article-outline-scroll";
    var nav = document.createElement("nav");
    nav.className = "article-outline-tree";
    nav.setAttribute("aria-label", "文章层级大纲");
    renderOutlineNav(nav, tree.children);
    scroll.appendChild(nav);
    rootDetails.appendChild(scroll);
    dock.appendChild(rootDetails);

    function updateActive() {
      if (!links.length) return;
      var y = window.scrollY + 96;
      var current = links[0];
      for (var i = 0; i < links.length; i++) {
        if (links[i].el.offsetTop <= y) current = links[i];
        else break;
      }
      links.forEach(function (item) {
        item.btn.classList.toggle("is-active", item === current);
      });
    }

    window.addEventListener("scroll", updateActive, { passive: true });
    updateActive();
  });
})();
