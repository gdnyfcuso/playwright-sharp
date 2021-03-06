using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlaywrightSharp.Helpers;
using PlaywrightSharp.Helpers.Linux;
using PlaywrightSharp.Transport;
using PlaywrightSharp.Transport.Channels;
using PlaywrightSharp.Transport.Converters;

namespace PlaywrightSharp.Transport
{
    internal class Connection : IDisposable
    {
        private readonly ConcurrentDictionary<string, IChannelOwner> _objects = new ConcurrentDictionary<string, IChannelOwner>();
        private readonly ConcurrentDictionary<string, ConnectionScope> _scopes = new ConcurrentDictionary<string, ConnectionScope>();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<IChannelOwner>> _waitingForObject = new ConcurrentDictionary<string, TaskCompletionSource<IChannelOwner>>();
        private readonly ConcurrentDictionary<int, ConnectionCallback> _callbacks = new ConcurrentDictionary<int, ConnectionCallback>();
        private readonly ConnectionScope _rootScript;
        private readonly Process _playwrightServerProcess;
        private readonly IConnectionTransport _transport;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<Connection> _logger;
        private readonly TaskQueue _queue = new TaskQueue();
        private int _lastId;

        public Connection(ILoggerFactory loggerFactory, TransportTaskScheduler scheduler)
        {
            _rootScript = CreateScope(string.Empty);

            _playwrightServerProcess = GetProcess();
            _playwrightServerProcess.StartInfo.Arguments = "--run";
            _playwrightServerProcess.Start();
            _playwrightServerProcess.Exited += (sender, e) => CloseAsync("Process exited");
            _transport = new StdIOTransport(_playwrightServerProcess, scheduler);
            _transport.MessageReceived += Transport_MessageReceived;
            _transport.TransportClosed += (sender, e) => CloseAsync(e.CloseReason);
            _loggerFactory = loggerFactory;
            _logger = _loggerFactory?.CreateLogger<Connection>();
        }

        /// <inheritdoc cref="IDisposable.Dispose"/>
        ~Connection() => Dispose(false);

        public bool IsClosed { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        internal static async Task InstallAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            using var process = GetProcess();
            process.StartInfo.RedirectStandardOutput = false;
            process.StartInfo.RedirectStandardInput = false;
            process.EnableRaisingEvents = true;
            process.StartInfo.Arguments = "--install";
            process.Exited += (sender, e) => tcs.TrySetResult(true);
            process.Start();

            await tcs.Task.ConfigureAwait(false);
        }

        internal ConnectionScope CreateScope(string guid)
        {
            var scope = new ConnectionScope(this, guid, _loggerFactory);
            _scopes.TryAdd(guid, scope);
            return scope;
        }

        internal void RemoveScope(string guid) => _scopes.TryRemove(guid, out _);

        internal void RemoveObject(string guid) => _objects.TryRemove(guid, out _);

        internal IChannelOwner GetObject(string guid)
        {
            _objects.TryGetValue(guid, out var result);
            return result;
        }

        internal JsonSerializerOptions GetDefaultJsonSerializerOptions(bool ignoreNullValues = false)
        {
            var options = JsonExtensions.GetNewDefaultSerializerOptions(ignoreNullValues);
            options.Converters.Add(new ChannelOwnerToGuidConverter(this));
            options.Converters.Add(new ChannelToGuidConverter(this));
            options.Converters.Add(new HttpMethodConverter());

            return options;
        }

        internal async Task<T> WaitForObjectWithKnownName<T>(string guid)
            where T : class
        {
            if (_objects.TryGetValue(guid, out var channel))
            {
                return channel as T;
            }

            var tcs = new TaskCompletionSource<IChannelOwner>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waitingForObject.TryAdd(guid, tcs);
            return await tcs.Task.ConfigureAwait(false) as T;
        }

        internal void OnObjectCreated(string guid, IChannelOwner result)
        {
            _objects.TryAdd(guid, result);
            if (_waitingForObject.TryRemove(guid, out var callback))
            {
                callback.TrySetResult(result);
            }
        }

        internal async Task<T> SendMessageToServerAsync<T>(
            string guid,
            string method,
            object args,
            bool ignoreNullValues = true,
            JsonSerializerOptions options = null,
            bool treatErrorPropertyAsError = true)
        {
            if (IsClosed)
            {
                throw new PlaywrightSharpException("Connection closed");
            }

            int id = Interlocked.Increment(ref _lastId);
            var tcs = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new ConnectionCallback
            {
                TaskCompletionSource = tcs,
                TreatErrorPropertyAsError = treatErrorPropertyAsError,
            };

            _callbacks.TryAdd(id, callback);

            await _queue.Enqueue(() =>
            {
                var message = new MessageRequest
                {
                    Id = id,
                    Guid = guid,
                    Method = method,
                    Params = args,
                };

                string messageString = JsonSerializer.Serialize(message, options ?? GetDefaultJsonSerializerOptions(ignoreNullValues));
                Debug.WriteLine($"pw:channel:command {messageString}");
                _logger?.LogInformation($"pw:channel:command {messageString}");

                return _transport.SendAsync(messageString);
            }).ConfigureAwait(false);

            var result = await tcs.Task.WithTimeout(Playwright.DefaultTimeout).ConfigureAwait(false);

            if (typeof(T) == typeof(JsonElement?))
            {
                return (T)(object)result;
            }
            else if (result == null)
            {
                return default;
            }
            else if (typeof(ChannelBase).IsAssignableFrom(typeof(T)) || typeof(ChannelBase[]).IsAssignableFrom(typeof(T)))
            {
                var enumerate = result.Value.EnumerateObject();

                return enumerate.Any()
                    ? enumerate.FirstOrDefault().Value.ToObject<T>(GetDefaultJsonSerializerOptions())
                    : default;
            }
            else
            {
                return result.Value.ToObject<T>(GetDefaultJsonSerializerOptions());
            }
        }

        private static Process GetProcess()
            => new Process
            {
                StartInfo =
                {
                    FileName = GetExecutablePath(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                },
            };

        private static string GetExecutablePath()
        {
            // This is not the final solution.
            string tempDirectory = new FileInfo(typeof(Playwright).Assembly.Location).Directory.FullName;
            string driver = "playwright-driver-win.exe";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                driver = "playwright-driver-macos";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                driver = "playwright-driver-linux";
            }

            string file = Path.Combine(tempDirectory, driver);
            ExtractDriver(file, driver);
            return file;
        }

        private static void ExtractDriver(string file, string driver)
        {
            using (var resource = typeof(Playwright).Assembly.GetManifestResourceStream($"PlaywrightSharp.Drivers.browsers.json"))
            {
                var fileInfo = new FileInfo(Path.Combine(new FileInfo(file).Directory.FullName, "browsers.json"));
                if (fileInfo.Exists)
                {
                    fileInfo.Delete();
                }

                using var fileStream = new FileStream(fileInfo.FullName, FileMode.OpenOrCreate, FileAccess.Write);
                resource.CopyTo(fileStream);
            }

            using (var resource = typeof(Playwright).Assembly.GetManifestResourceStream($"PlaywrightSharp.Drivers.{driver}"))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Exists)
                {
                    fileInfo.Delete();
                }

                using var fileStream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write);
                resource.CopyTo(fileStream);
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (LinuxSysCall.Chmod(file, LinuxSysCall.ExecutableFilePermissions) != 0)
                {
                    throw new PlaywrightSharpException($"Unable to chmod the driver ({Marshal.GetLastWin32Error()})");
                }
            }
        }

        private void Transport_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            var message = JsonSerializer.Deserialize<PlaywrightServerMessage>(e.Message, JsonExtensions.DefaultJsonSerializerOptions);

            if (message.Id.HasValue)
            {
                Debug.WriteLine($"pw:channel:response {e.Message}");
                _logger?.LogInformation($"pw:channel:response {e.Message}");

                if (_callbacks.TryRemove(message.Id.Value, out var callback))
                {
                    if (message.Error != null)
                    {
                        callback.TaskCompletionSource.TrySetException(CreateException(message.Error.Error));
                    }
                    else if (callback.TreatErrorPropertyAsError && message.Result?.TryGetProperty("error", out var _) == true)
                    {
                        callback.TaskCompletionSource.TrySetException(CreateException(message.Result?.GetProperty("error").ToString()));
                    }
                    else
                    {
                        callback.TaskCompletionSource.TrySetResult(message.Result);
                    }
                }

                return;
            }

            Debug.WriteLine($"pw:channel:event {e.Message}");
            _logger?.LogInformation($"pw:channel:event {e.Message}");

            try
            {
                if (message.Method == "__create__")
                {
                    _objects.TryGetValue(message.Guid, out var scopeObject);
                    var scope = scopeObject != null ? scopeObject.Scope : _rootScript;
                    var createObjectInfo = message.Params.Value.ToObject<CreateObjectInfo>(GetDefaultJsonSerializerOptions());
                    scope.CreateRemoteObject(createObjectInfo.Type, createObjectInfo.Guid, createObjectInfo.Initializer);

                    return;
                }

                _objects.TryGetValue(message.Guid, out var obj);
                obj?.Channel?.OnMessage(message.Method, message.Params);
            }
            catch (Exception ex)
            {
                CloseAsync(ex.ToString());
            }
        }

        private void CloseAsync(string reason)
        {
            if (!IsClosed)
            {
                foreach (var callback in _callbacks)
                {
                    callback.Value.TaskCompletionSource.TrySetException(new TargetClosedException(reason));
                }

                Dispose();
                IsClosed = true;
            }
        }

        private Exception CreateException(PlaywrightServerError error)
        {
            if (string.IsNullOrEmpty(error.Message))
            {
                return new PlaywrightSharpException(error.Value);
            }

            if (error.Name == "TimeoutError")
            {
                return new TimeoutException(error.Message);
            }

            if (error.Message.Contains("Target closed") || error.Message.Contains("The page has been closed."))
            {
                return new TargetClosedException(error.Message);
            }

            if (error.Message.Contains("Navigation failed because"))
            {
                return new NavigationException(error.Message);
            }

            return new PlaywrightSharpException(error.Message);
        }

        private Exception CreateException(string message)
        {
            if (message.Contains("Timeout") && message.Contains("ms exceeded"))
            {
                return new TimeoutException(message);
            }

            if (message.Contains("Target closed") || message.Contains("The page has been closed."))
            {
                return new TargetClosedException(message);
            }

            if (message.Contains("Navigation failed because"))
            {
                return new NavigationException(message);
            }

            return new PlaywrightSharpException(message);
        }

        private void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            _queue.Dispose();
            _transport.Close("Connection closed");

            try
            {
                _playwrightServerProcess?.Kill();
                _playwrightServerProcess?.Dispose();
            }
            catch
            {
            }
        }
    }
}
