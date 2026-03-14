using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lib.GAB.Tools;

namespace Lib.GAB.Docs
{
    /// <summary>
    /// Lightweight HTTP server that serves OpenAPI documentation for GABP tools.
    /// Serves /openapi.json (spec) and / or /docs (Swagger UI).
    /// </summary>
    public class DocsHttpServer : IDisposable
    {
        private readonly IToolRegistry _toolRegistry;
        private readonly string _title;
        private readonly string _version;
        private HttpListener _listener;
        private CancellationTokenSource _cts;
        private Task _serverTask;

        /// <summary>
        /// Port the HTTP server is listening on
        /// </summary>
        public int Port { get; private set; }

        /// <summary>
        /// Create a new documentation HTTP server for the given tool registry
        /// </summary>
        public DocsHttpServer(IToolRegistry toolRegistry, string title, string version)
        {
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _title = title;
            _version = version;
        }

        /// <summary>
        /// Start serving documentation on the specified port (0 for automatic)
        /// </summary>
        public void Start(int port = 0)
        {
            if (_listener != null) return;

            // If port is 0, find a free port
            if (port == 0)
            {
                var tempListener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                tempListener.Start();
                port = ((IPEndPoint)tempListener.LocalEndpoint).Port;
                tempListener.Stop();
            }

            Port = port;
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();

            _serverTask = Task.Run(() => ListenLoop(_cts.Token));
        }

        /// <summary>
        /// Stop the documentation server
        /// </summary>
        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
            _listener = null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { break; }
                catch { /* ignore transient errors */ }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url.AbsolutePath.TrimEnd('/');

                // CORS headers for local dev
                context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
                context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (context.Request.HttpMethod == "OPTIONS")
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    return;
                }

                switch (path)
                {
                    case "/openapi.json":
                        ServeOpenApiSpec(context);
                        break;
                    case "":
                    case "/docs":
                        ServeSwaggerUI(context);
                        break;
                    default:
                        context.Response.StatusCode = 404;
                        WriteResponse(context, "text/plain", "Not Found. Try /docs or /openapi.json");
                        break;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    context.Response.StatusCode = 500;
                    WriteResponse(context, "text/plain", $"Error: {ex.Message}");
                }
                catch { /* response already sent */ }
            }
        }

        private void ServeOpenApiSpec(HttpListenerContext context)
        {
            var tools = _toolRegistry.GetTools();
            var spec = OpenApiGenerator.Generate(tools, _title, _version);
            context.Response.StatusCode = 200;
            WriteResponse(context, "application/json", spec);
        }

        private void ServeSwaggerUI(HttpListenerContext context)
        {
            var html = SwaggerUiHtml.Replace("{{SPEC_URL}}", $"http://localhost:{Port}/openapi.json");
            context.Response.StatusCode = 200;
            WriteResponse(context, "text/html", html);
        }

        private static void WriteResponse(HttpListenerContext context, string contentType, string body)
        {
            var buffer = Encoding.UTF8.GetBytes(body);
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        private const string SwaggerUiHtml = @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <title>GABP Tools API Documentation</title>
    <link rel=""stylesheet"" href=""https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"">
    <style>
        body { margin: 0; padding: 0; }
        .topbar { display: none; }
    </style>
</head>
<body>
    <div id=""swagger-ui""></div>
    <script src=""https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js""></script>
    <script>
        SwaggerUIBundle({
            url: '{{SPEC_URL}}',
            dom_id: '#swagger-ui',
            deepLinking: true,
            defaultModelsExpandDepth: 1,
            defaultModelExpandDepth: 2,
            docExpansion: 'list',
            filter: true,
            showExtensions: true,
        });
    </script>
</body>
</html>";
    }
}
