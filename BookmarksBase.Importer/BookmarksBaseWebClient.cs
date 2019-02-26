﻿using System;
using System.Net;

namespace BookmarksBase.Importer
{
    public class BookmarksBaseWebClient : WebClient
    {
        readonly BookmarksImporter.Options _options;
        readonly CookieContainer _cookies = new CookieContainer();

        public BookmarksBaseWebClient(BookmarksImporter.Options options)
        {
            _options = options;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address) as HttpWebRequest;
            request.ProtocolVersion = HttpVersion.Version11;
            request.MaximumAutomaticRedirections = 100;
            request.AllowAutoRedirect = true;
            request.Timeout = 5000;
            request.KeepAlive = false;
            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:63.0) Gecko/20100101 Firefox/63.0";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            request.Headers["Accept-Language"] = "pl,en-US;q=0.7,en;q=0.3";
            request.Headers["Accept-Encoding"] = "gzip, deflate";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            // It turns out, when these properties are set and system wide SOCKS proxy is configured,
            // the connection bypasses the proxy. We need an option to prevent this.
            if (!_options.SockProxyFriendly)
            {
                request.CookieContainer = _cookies;
            }
            return request;
        }
    }
}