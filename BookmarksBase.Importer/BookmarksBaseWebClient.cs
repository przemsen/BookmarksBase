using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using BookmarksBase.Importer;

namespace BookmarksBase.Importer
{
    public class BookmarksBaseWebClient : WebClient
    {
        private readonly BookmarksImporter.Options _options;

        public BookmarksBaseWebClient(BookmarksImporter.Options options)
        {
            _options = options;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address) as HttpWebRequest;

            // It turns out, when these properties are set and system wide SOCKS proxy is configured,
            // the connection bypasses the proxy. We need an option to prevent this.
            if (!_options.SockProxyFriendly)
            {
                // Try to mimic web browser as much as we can.
                // TODO: maybe more sophisticated crafting of HTTP headers
                request.MaximumAutomaticRedirections = 100;
                request.CookieContainer = new CookieContainer();
                request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; rv:37.0) Gecko/20100101 Firefox/37.0";
                request.Timeout = 5000;
            }

            return request;
        }
    }
}