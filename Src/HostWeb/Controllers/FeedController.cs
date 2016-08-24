using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.UI;
using System.Xml;

namespace GoogleResearchWeb2Feed.Controllers
{
    public class FeedController : Controller
    {
        // GET /Feed/Rss
        [OutputCache(Duration = 3600, Location = OutputCacheLocation.Server)]
        public async Task<ActionResult> Rss()
        {
            var grWeb2Feed = new GoogleResearchWeb2Feed();

            using (var sw = new StringWriter())
            {
                var formatter = new Rss20FeedFormatter(await grWeb2Feed.GetFeed());

                using (var xml = new XmlTextWriter(sw))
                {
                    formatter.WriteTo(xml);
                }
                return Content(sw.ToString(), "application/rss+xml", Encoding.UTF8);
            }
        }
    }
}
