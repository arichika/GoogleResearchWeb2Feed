using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Mvc;
using System.Xml;

namespace GoogleResearchWeb2Feed.Controllers
{
    public class FeedController : Controller
    {
        // GET /Feed/Rss
        public async Task<ActionResult> Rss()
        {
            var grWeb2Feed = new GoogleResearchWeb2Feed();

            using (var sw = new StringWriter())
            {
                var xml = new XmlTextWriter(sw);
                (await grWeb2Feed.GetFeed()).SaveAsRss20(xml);
                return Content(sw.ToString(), "application/rss+xml", Encoding.UTF8);
            }
        }
    }
}
