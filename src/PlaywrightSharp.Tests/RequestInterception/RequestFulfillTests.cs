using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using PlaywrightSharp.Tests.BaseTests;
using PlaywrightSharp.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace PlaywrightSharp.Tests.RequestInterception
{
    ///<playwright-file>interception.spec.js</playwright-file>
    ///<playwright-describe>request.fulfill</playwright-describe>
    [Collection(TestConstants.TestFixtureBrowserCollectionName)]
    public class RequestFulfillTests : PlaywrightSharpPageBaseTest
    {
        /// <inheritdoc/>
        public RequestFulfillTests(ITestOutputHelper output) : base(output)
        {
        }

        ///<playwright-file>interception.spec.js</playwright-file>
        ///<playwright-describe>request.fulfill</playwright-describe>
        ///<playwright-it>should work</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldWork()
        {
            await Page.RouteAsync("**/*", (route, request) =>
            {
                route.FulfillAsync(new RouteFilfillResponse
                {
                    Status = HttpStatusCode.Created,
                    Headers = new Dictionary<string, string>
                    {
                        ["foo"] = "bar"
                    },
                    ContentType = "text/html",
                    Body = "Yo, page!"
                });
            });
            var response = await Page.GoToAsync(TestConstants.EmptyPage);
            Assert.Equal(HttpStatusCode.Created, response.Status);
            Assert.Equal("bar", response.Headers["foo"]);
            Assert.Equal("Yo, page!", await Page.EvaluateAsync<string>("() => document.body.textContent"));
        }

        /// <summary>
        /// In Playwright this method is called ShouldWorkWithStatusCode422.
        /// I found that status 422 is not available in all .NET runtime versions (see https://github.com/dotnet/core/blob/4c4642d548074b3fbfd425541a968aadd75fea99/release-notes/2.1/Preview/api-diff/preview2/2.1-preview2_System.Net.md)
        /// As the goal here is testing HTTP codes that are not in Chromium (see https://cs.chromium.org/chromium/src/net/http/http_status_code_list.h?sq=package:chromium) we will use code 426: Upgrade Required
        /// </summary>
        ///<playwright-file>interception.spec.js</playwright-file>
        ///<playwright-describe>request.fulfill</playwright-describe>
        ///<playwright-it>should work with status code 422</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldWorkWithStatusCode422()
        {
            await Page.RouteAsync("**/*", (route, request) =>
            {
                route.FulfillAsync(new RouteFilfillResponse
                {
                    Status = HttpStatusCode.UpgradeRequired,
                    Body = "Yo, page!"
                });
            });
            var response = await Page.GoToAsync(TestConstants.EmptyPage);
            Assert.Equal(HttpStatusCode.UpgradeRequired, response.Status);
            Assert.Equal("Upgrade Required", response.StatusText);
            Assert.Equal("Yo, page!", await Page.EvaluateAsync<string>("() => document.body.textContent"));
        }

        ///<playwright-file>interception.spec.js</playwright-file>
        ///<playwright-describe>request.fulfill</playwright-describe>
        ///<playwright-it>should allow mocking binary responses</playwright-it>
        [Fact(Skip = "We need screenshots for this")]
        public async Task ShouldAllowMockingBinaryResponses()
        {
            await Page.RouteAsync("**/*", (route, request) =>
            {
                byte[] imageBuffer = File.ReadAllBytes(TestUtils.GetWebServerFile("pptr.png"));
                route.FulfillAsync(new RouteFilfillResponse
                {
                    ContentType = "image/png",
                    BodyContent = imageBuffer
                });
            });
            await Page.EvaluateAsync(@"PREFIX => {
                const img = document.createElement('img');
                img.src = PREFIX + '/does-not-exist.png';
                document.body.appendChild(img);
                return new Promise(fulfill => img.onload = fulfill);
            }", TestConstants.ServerUrl);
            var img = await Page.QuerySelectorAsync("img");
            Assert.True(ScreenshotHelper.PixelMatch("mock-binary-response.png", await img.ScreenshotAsync()));
        }

        ///<playwright-file>interception.spec.js</playwright-file>
        ///<playwright-describe>request.fulfill</playwright-describe>
        ///<playwright-it>should allow mocking svg with charset</playwright-it>
        [Fact(Skip = "We need screenshots for this")]
        public void ShouldAllowMockingSvgWithCharset()
        {
        }

        ///<playwright-file>interception.spec.js</playwright-file>
        ///<playwright-describe>request.fulfill</playwright-describe>
        ///<playwright-it>should work with file path</playwright-it>
        [Fact(Skip = "We need screenshots for this")]
        public async Task ShouldWorkWithFilePath()
        {
            await Page.RouteAsync("**/*", (route, request) =>
            {
                route.FulfillAsync(new RouteFilfillResponse
                {
                    ContentType = "shouldBeIgnored",
                    Path = TestUtils.GetWebServerFile("pptr.png")
                });
            });

            await Page.EvaluateAsync(@"PREFIX => {
                const img = document.createElement('img');
                img.src = PREFIX + '/does-not-exist.png';
                document.body.appendChild(img);
                return new Promise(fulfill => img.onload = fulfill);
            }", TestConstants.ServerUrl);
            var img = await Page.QuerySelectorAsync("img");
            Assert.True(ScreenshotHelper.PixelMatch("mock-binary-response.png", await img.ScreenshotAsync()));
        }

        ///<playwright-file>interception.spec.js</playwright-file>
        ///<playwright-describe>request.fulfill</playwright-describe>
        ///<playwright-it>should stringify intercepted request response headers</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldStringifyInterceptedRequestResponseHeaders()
        {
            await Page.RouteAsync("**/*", (route, request) =>
            {
                route.FulfillAsync(new RouteFilfillResponse
                {
                    Status = HttpStatusCode.OK,
                    Headers = new Dictionary<string, string>
                    {
                        ["foo"] = "true"
                    },
                    Body = "Yo, page!"
                });
            });

            var response = await Page.GoToAsync(TestConstants.EmptyPage);
            Assert.Equal(HttpStatusCode.OK, response.Status);
            Assert.Equal("true", response.Headers["foo"]);
            Assert.Equal("Yo, page!", await Page.EvaluateAsync<string>("() => document.body.textContent"));
        }

        ///<playwright-file>interception.spec.js</playwright-file>
        ///<playwright-describe>request.fulfill</playwright-describe>
        ///<playwright-it>should not modify the headers sent to the server</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldNotModifyTheHeadersSentToTheServer()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            var interceptedRequests = new List<Dictionary<string, string>>();

            await Page.GoToAsync(TestConstants.ServerUrl + "/unused");

            Server.SetRoute("/something", ctx =>
            {
                interceptedRequests.Add(ctx.Request.Headers.ToDictionary());
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                return ctx.Response.WriteAsync("done");
            });

            string text = await Page.EvaluateAsync<string>(@"async url => {
                const data = await fetch(url);
                return data.text();
            }", TestConstants.CrossProcessUrl + "/something");

            Assert.Equal("done", text);

            IRequest playwrightRequest = null;

            await Page.RouteAsync(TestConstants.CrossProcessUrl + "/something", (route, request) =>
            {
                playwrightRequest = request;
                route.ContinueAsync(new RouteContinueOverrides { Headers = request.Headers });
            });

            string textAfterRoute = await Page.EvaluateAsync<string>(@"async url => {
                const data = await fetch(url);
                return data.text();
            }", TestConstants.CrossProcessUrl + "/something");

            Assert.Equal("done", textAfterRoute);

            Assert.Equal(2, interceptedRequests.Count);
            Assert.Equal(interceptedRequests[1].OrderBy(kv => kv.Key), interceptedRequests[0].OrderBy(kv => kv.Key));
        }

        ///<playwright-file>interception.spec.js</playwright-file>
        ///<playwright-describe>request.fulfill</playwright-describe>
        ///<playwright-it>should include the origin header</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldIncludeTheOriginHeader()
        {
            await Page.GoToAsync(TestConstants.EmptyPage);
            IRequest interceptedRequest = null;

            await Page.RouteAsync(TestConstants.CrossProcessUrl + "/something", (route, request) =>
            {
                interceptedRequest = request;
                route.FulfillAsync(new RouteFilfillResponse
                {
                    Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                    ContentType = "text/plain",
                    Body = "done",
                });
            });

            string text = await Page.EvaluateAsync<string>(@"async url => {
                const data = await fetch(url);
                return data.text();
            }", TestConstants.CrossProcessUrl + "/something");

            Assert.Equal("done", text);
            Assert.Equal(TestConstants.ServerUrl, interceptedRequest.Headers["origin"]);
        }
    }
}
