using CarlosServer.Utility;
using System.Net.WebSockets;
using System.Text;
using System.Timers;

namespace CarlosServer
{
    public class Program
    {
        private const int KALOS_INTERVAL = 100;

        private const int KALOS_MAXTIME_MINUTES = 2;

        private static readonly System.Timers.Timer _KalosTimer = new(KALOS_INTERVAL);

        private static TimeSpan _KalosTime = TimeSpan.FromMinutes(KALOS_MAXTIME_MINUTES);

        private static readonly HashSet<WebSocket> _ActiveSockets = new();

        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllersWithViews();

            WebApplication app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseWebSockets();
            app.Use(async (context, nextRequest) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    await HandleWSRequest(context);
                }
                else
                {
                    await nextRequest(context);
                }
            });

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            _KalosTimer.Elapsed += new ElapsedEventHandler(OnKalosTimerElapsed);

            app.Run();
        }

        public static async Task HandleWSRequest(HttpContext context)
        {
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();

            _ActiveSockets.Add(socket);

            int payloadSize = 1024;
            bool connectionIsAlive = true;
            List<byte> webSocketPayload = new(payloadSize);
            byte[] tempMessage = new byte[payloadSize];

            //Starta ny task som läser server
            while (connectionIsAlive)
            {
                webSocketPayload.Clear();

                WebSocketReceiveResult? webSocketResponse;

                do
                {
                    try
                    {
                        webSocketResponse = await socket.ReceiveAsync(tempMessage, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{ex.Message}\nRemoving the socket");
                        _ActiveSockets.Remove(socket);
                        return;
                    }

                    webSocketPayload.AddRange(new ArraySegment<byte>(tempMessage, 0, webSocketResponse.Count));
                }
                while (!webSocketResponse.EndOfMessage);

                if (webSocketResponse.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(webSocketPayload.ToArray());

                    if (!Enum.TryParse(message, out WSTimerMessage timerMessage))
                    {
                        Console.WriteLine($"Could not parse the message: {message}");
                        continue;
                    }

                    switch (timerMessage)
                    {
                        case WSTimerMessage.Start:
                            _KalosTimer.Start();
                            break;
                        case WSTimerMessage.Stop:
                            _KalosTimer.Stop();
                            break;
                        case WSTimerMessage.Reset:
                            _KalosTimer.Stop();
                            _KalosTime = TimeSpan.FromMinutes(KALOS_MAXTIME_MINUTES);
                            await PushTimes();
                            break;
                        case WSTimerMessage.ResetAndStart:
                            _KalosTimer.Stop();
                            _KalosTime = TimeSpan.FromMinutes(KALOS_MAXTIME_MINUTES);
                            _KalosTimer.Start();
                            break;
                    }
                }
                else if (webSocketResponse.MessageType == WebSocketMessageType.Close)
                {
                    connectionIsAlive = false;
                    _ActiveSockets.Remove(socket);
                }
            }
        }

        private static async Task PushTimes()
        {
            byte[] encodedMessage = Encoding.UTF8.GetBytes(new DateTime(_KalosTime.Ticks).ToString("mm:ss.fff"));
            ArraySegment<byte> buffer = new(encodedMessage, 0, encodedMessage.Length);

            foreach (WebSocket activeSocket in _ActiveSockets)
            {
                if (activeSocket != null)
                {
                    try
                    {
                        await activeSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);

                        if (activeSocket.State != WebSocketState.Open)
                        {
                            Console.WriteLine("Removing socket..");
                            _ActiveSockets.Remove(activeSocket);
                        }

                        continue;
                    }
                }
            }
        }

        private static async void OnKalosTimerElapsed(object? obj, ElapsedEventArgs args)
        {
            if (_KalosTime == TimeSpan.Zero)
            {
                return;
            }

            _KalosTime -= TimeSpan.FromMilliseconds(KALOS_INTERVAL);

            await PushTimes();
        }
    }
}