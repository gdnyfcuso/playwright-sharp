using System;
using System.Linq;
using System.Threading.Tasks;
using PlaywrightSharp.Tests.BaseTests;
using PlaywrightSharp.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace PlaywrightSharp.Tests.Frame
{
    ///<playwright-file>waittask.spec.js</playwright-file>
    ///<playwright-describe>Frame.waitForSelector xpath</playwright-describe>
    [Collection(TestConstants.TestFixtureBrowserCollectionName)]
    public class WaitForSelectorXPathTests : PlaywrightSharpPageBaseTest
    {
        const string AddElement = "tag => document.body.appendChild(document.createElement(tag))";

        /// <inheritdoc/>
        public WaitForSelectorXPathTests(ITestOutputHelper output) : base(output)
        {
        }

        ///<playwright-file>waittask.spec.js</playwright-file>
        ///<playwright-describe>Frame.waitForSelector xpath</playwright-describe>
        ///<playwright-it>should support some fancy xpath</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldSupportSomeFancyXpath()
        {
            await Page.SetContentAsync("<p>red herring</p><p>hello  world  </p>");
            var waitForXPath = Page.WaitForSelectorAsync("//p[normalize-space(.)=\"hello world\"]");
            Assert.Equal("hello  world  ", await Page.EvaluateAsync<string>("x => x.textContent", await waitForXPath));
        }

        ///<playwright-file>waittask.spec.js</playwright-file>
        ///<playwright-describe>Frame.waitForSelector xpath</playwright-describe>
        ///<playwright-it>should respect timeout</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldRespectTimeout()
        {
            var exception = await Assert.ThrowsAsync<TimeoutException>(()
                    => Page.WaitForSelectorAsync("//div", WaitForState.Attached, timeout: 3000));

            Assert.Contains("Timeout 3000ms exceeded", exception.Message);
            Assert.Contains("waiting for selector \"//div\"", exception.Message);
        }

        ///<playwright-file>waittask.spec.js</playwright-file>
        ///<playwright-describe>Frame.waitForSelector xpath</playwright-describe>
        ///<playwright-it>should run in specified frame</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldRunInSpecifiedFrame()
        {
            await FrameUtils.AttachFrameAsync(Page, "frame1", TestConstants.EmptyPage);
            await FrameUtils.AttachFrameAsync(Page, "frame2", TestConstants.EmptyPage);
            var frame1 = Page.Frames.First(f => f.Name == "frame1");
            var frame2 = Page.Frames.First(f => f.Name == "frame2");
            var waitForXPathPromise = frame2.WaitForSelectorAsync("//div", WaitForState.Attached);
            await frame1.EvaluateAsync(AddElement, "div");
            await frame2.EvaluateAsync(AddElement, "div");
            var eHandle = await waitForXPathPromise;
            Assert.Equal(frame2, await eHandle.GetOwnerFrameAsync());
        }

        ///<playwright-file>waittask.spec.js</playwright-file>
        ///<playwright-describe>Frame.waitForSelector xpath</playwright-describe>
        ///<playwright-it>should throw when frame is detached</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldThrowWhenFrameIsDetached()
        {
            await FrameUtils.AttachFrameAsync(Page, "frame1", TestConstants.EmptyPage);
            var frame = Page.FirstChildFrame();
            var waitPromise = frame.WaitForSelectorAsync("//*[@class=\"box\"]");
            await FrameUtils.DetachFrameAsync(Page, "frame1");
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => waitPromise);
            Assert.Contains("waitForFunction failed: frame got detached.", exception.Message);
        }

        ///<playwright-file>waittask.spec.js</playwright-file>
        ///<playwright-describe>Frame.waitForSelector xpath</playwright-describe>
        ///<playwright-it>should return the element handle</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldReturnTheElementHandle()
        {
            var waitForXPath = Page.WaitForSelectorAsync("//*[@class=\"zombo\"]");
            await Page.SetContentAsync("<div class='zombo'>anything</div>");
            Assert.Equal("anything", await Page.EvaluateAsync<string>("x => x.textContent", await waitForXPath));
        }

        ///<playwright-file>waittask.spec.js</playwright-file>
        ///<playwright-describe>Frame.waitForSelector xpath</playwright-describe>
        ///<playwright-it>should allow you to select an element with single slash</playwright-it>
        [Fact(Timeout = PlaywrightSharp.Playwright.DefaultTimeout)]
        public async Task ShouldAllowYouToSelectAnElementWithSingleSlash()
        {
            await Page.SetContentAsync("<div>some text</div>");
            var waitForXPath = Page.WaitForSelectorAsync("//html/body/div");
            Assert.Equal("some text", await Page.EvaluateAsync<string>("x => x.textContent", await waitForXPath));
        }
    }
}
