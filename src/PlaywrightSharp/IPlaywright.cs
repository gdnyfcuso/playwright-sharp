using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PlaywrightSharp
{
    /// <summary>
    /// IPlaywright provides methods to interact with the playwright server.
    /// </summary>
    public interface IPlaywright : IDisposable
    {
        /// <summary>
        /// Gets the Chromium browser type from the playwright server.
        /// </summary>
        IBrowserType Chromium { get; }

        /// <summary>
        /// Gets the Firefox browser type from the playwright server.
        /// </summary>
        IBrowserType Firefox { get; }

        /// <summary>
        /// Gets the Webkit browser type from the playwright server.
        /// </summary>
        IBrowserType Webkit { get; }

        /// <summary>
        /// Returns a list of devices to be used with <see cref="IBrowser.NewContextAsync(BrowserContextOptions)"/>.
        /// </summary>
        public IReadOnlyDictionary<string, DeviceDescriptor> Devices { get; }

        /// <summary>
        /// Gets a <see cref="IBrowserType"/>.
        /// </summary>
        /// <param name="browserType"><see cref="IBrowserType"/> name. You can get the names from <see cref="BrowserType"/>.
        /// e.g.: <see cref="BrowserType.Chromium"/>, <see cref="BrowserType.Firefox"/> or <see cref="BrowserType.Webkit"/>.
        /// </param>
        public IBrowserType this[string browserType] { get; }
    }
}
