using System;
using System.Net;
using System.Threading.Tasks;

namespace BookmarksBase.Importer
{
    public class BookmarksBaseWebClient : WebClient
    {
        // readonly BookmarksImporter.Options _options;
        readonly CookieContainer _cookies = new CookieContainer();
        const int MY_TIMEOUT = 15000;
        const int WEBCLIENT_TIMEOUT = 14000;
        const int SMALL_TIMEOUT_FOR_RETRY = 10000;
        public static int CustomTimeout { get; set; } = MY_TIMEOUT;

        public BookmarksBaseWebClient(BookmarksImporter.Options options)
        {
            // _options = options;
        }

        public async Task<byte[]> DownloadAsync(string url, bool smallTimeoutForRetry)
        {
            var timeoutTask = Task.Delay(smallTimeoutForRetry ? SMALL_TIMEOUT_FOR_RETRY : CustomTimeout);
            var mainTask = DownloadDataTaskAsync(url);

            await Task.WhenAny(timeoutTask, mainTask).ConfigureAwait(false);

            if (timeoutTask.Status == TaskStatus.RanToCompletion && mainTask.Status != TaskStatus.RanToCompletion)
            {
                throw new Exception("Internal timeout exceeded");
            }

            return mainTask.Result;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address) as HttpWebRequest;

            request.ProtocolVersion = HttpVersion.Version11;
            request.MaximumAutomaticRedirections = 100;
            request.AllowAutoRedirect = true;
            request.Timeout = WEBCLIENT_TIMEOUT;
            request.KeepAlive = false;
            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; Win64; x64; rv:63.0) Gecko/20100101 Firefox/63.0";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            request.Headers["Accept-Language"] = "pl,en-US;q=0.7,en;q=0.3";
            request.Headers["Accept-Encoding"] = "gzip, deflate";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.CookieContainer = _cookies;

            return request;
        }
    }
}