using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;

namespace GoogleResearchWeb2Feed
{
    public class GoogleResearchWeb2Feed
    {
        private static readonly string GrFqdn = "https://research.google.com";
        private static readonly string GrCrawlingRoot = "/pubs/papers.html";

        private static readonly Regex RegexArticldId = new Regex("/([a-zA-Z0-9]*).html$");
        private static readonly char[] TrimChars = new[] { '"', ' ', '\r', '\n' };

        private static readonly int MaxItemsPerArea = 10;
        private static readonly int MaxItemsPerFeed = 50;

        private static TimeSpan _cacheTimeSpan;

        private static  SyndicationFeed _resultLastSyndicationFeed;
        private static DateTime _resultLastDateTimeUtc = DateTime.MinValue;

        public GoogleResearchWeb2Feed(TimeSpan? cacheTimeSpan = null)
        {
            _cacheTimeSpan = cacheTimeSpan ?? TimeSpan.FromHours(1);
        }

        public async Task<SyndicationFeed> GetFeed(bool useCache = true)
        {
            if (useCache != false && _resultLastSyndicationFeed != null && _resultLastDateTimeUtc + _cacheTimeSpan >= DateTime.UtcNow)
                return _resultLastSyndicationFeed;

            var pubs = await GetPubs();
            _resultLastSyndicationFeed = await MakeFeed(pubs.Select(p => p.Value), GrFqdn);  // 2回とっても困りはしないので雑にキャッシュ
            _resultLastDateTimeUtc = DateTime.UtcNow;
            return _resultLastSyndicationFeed;
        }

        private static async Task<ConcurrentDictionary<string, Article>> GetPubs()
        {
            var pubs = new ConcurrentDictionary<string, Article>();

            var website = new HtmlWeb();
            var rootDoc = website.Load(GrFqdn + GrCrawlingRoot);
            var areas = rootDoc.DocumentNode.SelectNodes("//a[contains(@class,'research-area-title')]")
                .Select(node => new Uri(GrFqdn + node.Attributes["href"].Value)).ToList();

            foreach (var area in areas)
            {
                var areaDoc = website.Load(area.OriginalString);
                var nodes = areaDoc.DocumentNode.SelectNodes("//ul[contains(@class,'pub-list')]/li");
                foreach (var item in nodes.Select((node, i) => new { node, i }))
                {
                    var a = await GetArticles(item.node);
                    pubs.TryAdd(a.Id, a); // if the article duplicate, to skip.

                    if (item.i == MaxItemsPerArea)
                        break;
                }
            }

            return pubs;
        }

        private static async Task<Article> GetArticles(HtmlNode node)
        {
            // has search icon ?
            var searchUrl = node.SelectNodes("./a[contains(@class,'search-icon')]")?.FirstOrDefault()?.Attributes["href"]?.Value;

            // has pdf icon ?
            var pdfUrl = node.SelectNodes("./a[contains(@class,'pdf-icon')]")?.FirstOrDefault()?.Attributes["href"]?.Value;
            pdfUrl = pdfUrl?.IndexOf(@"http://", StringComparison.OrdinalIgnoreCase) < 0 ? GrFqdn + pdfUrl : pdfUrl;

            // has abstract icon ? (== abstract url)
            var abstractUrl = node.SelectNodes("./a[contains(@class,'abstract-icon')]")?.FirstOrDefault()?.Attributes["href"]?.Value;
            abstractUrl = abstractUrl?.IndexOf(@"http://", StringComparison.OrdinalIgnoreCase) < 0 ? GrFqdn + abstractUrl : abstractUrl;

            // get title
            var title = node.SelectNodes("./p[@class='pub-title']").FirstOrDefault()?.InnerText.Trim(TrimChars);

            // create appendix
            var appendix = node.SelectNodes("./p[@class!='pub-title']")?.Aggregate(string.Empty, (current, addNode) => current + addNode.InnerText.Trim(TrimChars));

            // create id
            var matchGroups = RegexArticldId.Match(abstractUrl ?? "").Groups;
            var id = matchGroups.Count == 2
                    ? matchGroups[1].Value
                    : new SHA256CryptoServiceProvider().ComputeHash(Encoding.UTF8.GetBytes(title ?? "")).Aggregate(string.Empty, (c, v) => c + $"{v:X2}");

            var article = new Article
            {
                Id = id,
                Title = title,
                Uri = (abstractUrl == null) ? null : new Uri(abstractUrl),
                SearchUri = (searchUrl == null) ? null : new Uri(searchUrl),
                PdfUri = (pdfUrl == null) ? null : new Uri(pdfUrl),
                Appendix = appendix,
                LastUpdateDateTime = null,
            };

            if (article.Uri != null)
                await AddAbstract(article);
            else
                article.LastUpdateDateTime = DateTimeOffset.UtcNow;

            return article;
        }

        private static async Task AddAbstract(Article article)
        {
            var httpClient = new HttpClient();
            var content = (await httpClient.GetAsync(article.Uri.OriginalString)).Content;
            var body = await content.ReadAsStringAsync();
            article.LastUpdateDateTime = content.Headers.LastModified;

            var abstractDoc = new HtmlDocument();
            abstractDoc.LoadHtml(body);
            var node = abstractDoc.DocumentNode.SelectNodes("//div[contains(@class,'abstract')]")?.FirstOrDefault();
            article.Abstract = node?.InnerText?.Trim(TrimChars);
        }

        private static async Task<SyndicationFeed> MakeFeed(IEnumerable<Article> articles, string feedId)
        {
            var feed = new SyndicationFeed
            {
                Id = feedId,
                Title = new TextSyndicationContent("Google Research"),
                Description = new TextSyndicationContent("GoogleResearchWeb2Feed"),
                Copyright = new TextSyndicationContent("PUBLIC DOMAIN"),
                LastUpdatedTime = new DateTimeOffset(DateTime.Now),
                Generator = "GoogleResearchWeb2Feed",
                ImageUrl = new Uri("https://research.google.com/favicon.ico"),
            };

            var items = new List<SyndicationItem>();

            foreach (var article in articles)
            {
                // TODO: Fill the item content
                var htmlContent = article.Abstract;

                var item = new SyndicationItem
                {
                    Id = article.Uri?.OriginalString ?? article.Id,
                    Title = new TextSyndicationContent(article.Title),
                    LastUpdatedTime = article.LastUpdateDateTime ?? DateTimeOffset.UtcNow,
                    PublishDate = article.LastUpdateDateTime ?? DateTimeOffset.UtcNow,
                    Content = new TextSyndicationContent(htmlContent, TextSyndicationContentKind.Html),
                };

                if(article.Uri != null)
                    item.Links.Add(new SyndicationLink(article.Uri));

                items.Add(item);

                if (items.Count >= MaxItemsPerFeed)
                    break;
            }

            feed.Items = items;
            return feed;
        }

        private class Article
        {
            public string Id { get; set; }
            public string Title { get; set; }
            public string Abstract { get; set; }
            public Uri Uri { get; set; }
            public Uri SearchUri { get; set; }
            public Uri PdfUri { get; set; }
            public string Appendix { get; set; }
            public DateTimeOffset? LastUpdateDateTime { get; set; }
        }
    }
}
