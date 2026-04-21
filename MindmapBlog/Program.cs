namespace MindmapBlog;

internal static class Program
{
    public static int Main(string[] args)
    {
        var scanDir = Path.Combine(Environment.CurrentDirectory, "DemoMindmapSystem");
        var outDir = Path.Combine(Environment.CurrentDirectory, "dist");
        string? siteBaseUrl = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scan":
                case "-s":
                    if (i + 1 >= args.Length) return Fail("缺少 --scan 的路径参数。");
                    scanDir = Path.GetFullPath(args[++i]);
                    break;
                case "--out":
                case "-o":
                    if (i + 1 >= args.Length) return Fail("缺少 --out 的路径参数。");
                    outDir = Path.GetFullPath(args[++i]);
                    break;
                case "--base-url":
                case "-u":
                    if (i + 1 >= args.Length) return Fail("缺少 --base-url 的网址参数（部署后的站点根 URL）。");
                    siteBaseUrl = args[++i].Trim().TrimEnd('/');
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    return 0;
                default:
                    return Fail($"未知参数：{args[i]}（使用 --help 查看用法）");
            }
        }

        if (!Directory.Exists(scanDir))
        {
            Console.Error.WriteLine($"扫描目录不存在：{scanDir}");
            return 2;
        }

        var mmFiles = Directory.GetFiles(scanDir, "*.mm", SearchOption.AllDirectories);
        if (mmFiles.Length == 0)
        {
            Console.WriteLine($"未在 {scanDir} 下找到任何 .mm 文件。");
            return 0;
        }

        var articles = new List<BlogArticle>();
        foreach (var file in mmFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var list = MindmapParser.ExtractArticles(file);
                articles.AddRange(list);
                if (list.Count > 0)
                    Console.WriteLine($"{file} → {list.Count} 篇文章");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"跳过 {file}：{ex.Message}");
            }
        }

        if (articles.Count == 0)
        {
            Console.WriteLine("没有解析到文章。");
            return 0;
        }

        Directory.CreateDirectory(outDir);
        new StaticSiteGenerator().Generate(articles, outDir, scanDir, siteBaseUrl);
        Console.WriteLine($"已生成 {articles.Count} 篇文章 → {outDir}");
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
 mindmap-blog — 从 FreeMind / Docear .mm 生成静态站点

 用法:
   dotnet run --project MindmapBlog [选项]

 约定:
   - 仅发布根节点带互联网图标（FreeMind 的 internet 图标）及子节点正文
   - 书签从节点「明细 / 笔记」正文中解析 #话题
   - 右侧「图册」展示各篇文章正文中的配图，并链到对应文章与图在文中的位置

 选项:
   -s, --scan <目录>   递归扫描 .mm（默认: ./DemoMindmapSystem）
   -o, --out <目录>    输出目录（默认: ./dist）
   -u, --base-url <URL> 部署后的站点根地址，用于 RSS 内绝对链接（可选）
   -h, --help          显示帮助
""");
    }
}
