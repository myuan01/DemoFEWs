using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using NAudio.Wave;

namespace DemoFEWs
{

    class SessionID
    {
        public string session_id { get; set; }
    }

    public class WebSocketWrapper
    {
        private const int ReceiveChunkSize = 4096;
        private const int SendChunkSize = 4096;

        private readonly ClientWebSocket _ws;
        private readonly Uri _uri;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly CancellationToken _cancellationToken;

        private Action<WebSocketWrapper> _onConnected;
        private Action<string, WebSocketWrapper> _onMessage;
        private Action<WebSocketWrapper> _onDisconnected;

        protected WebSocketWrapper(string uri)
        {
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.Zero;

            string username = "username_1";
            string password = "test_password_1";

            byte[] encodedCredentials = Encoding.UTF8.GetBytes($"{username}:{password}");

            string base64EncodedCredentials = Convert.ToBase64String(encodedCredentials);
            Console.WriteLine(base64EncodedCredentials);

            _ws.Options.SetRequestHeader("Authorization", $"Basic {base64EncodedCredentials}");

            _uri = new Uri(uri);
            _cancellationToken = _cancellationTokenSource.Token;
        }

        //----------------------------------------------------------------------------------------->
        // Creates a new instance.
        //      uri: The URI of the WebSocket server
        //----------------------------------------------------------------------------------------->
        public static WebSocketWrapper Create(string uri)
        {
            return new WebSocketWrapper(uri);
        }

        //----------------------------------------------------------------------------------------->
        // Connects to WebSocket server
        //      public interface
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper Connect()
        {
            ConnectAsync();
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Set the Action to call when the connection has been established.
        //      onConnect: The Action to call
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper OnConnect(Action<WebSocketWrapper> onConnect)
        {
            _onConnected = onConnect;
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Disconnects from WebSocket server.
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper Disconnect()
        {
            if ((_ws.State == WebSocketState.Open))
            {
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, _cancellationToken);
                CallOnDisconnected();
            }
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Set the Action to call when the connection has been terminated.
        //      onDisconnect: The Action to call
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper OnDisconnect(Action<WebSocketWrapper> onDisconnect)
        {
            _onDisconnected = onDisconnect;
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // send binary data to WebSocket server
        //      public interface
        //----------------------------------------------------------------------------------------->
        public void SendBytes(byte[] bytes)
        {
            SendBytesAsync(bytes);
        }

        //----------------------------------------------------------------------------------------->
        // Set the Action to call when a messages has been received.
        //      onMessage: The Action to call
        //----------------------------------------------------------------------------------------->
        public WebSocketWrapper OnMessage(Action<string, WebSocketWrapper> onMessage)
        {
            _onMessage = onMessage;
            return this;
        }

        //----------------------------------------------------------------------------------------->
        // Send a message to the WebSocket server
        //      public interface
        //      message: The text message to send
        //----------------------------------------------------------------------------------------->
        public void SendMessage(string message)
        {
            SendMessageAsync(message);
        }

        //----------------------------------------------------------------------------------------->
        // Send a message to the WebSocket server
        //      actual implementation for the associated public interface
        //      message: The text message to send
        //----------------------------------------------------------------------------------------->
        private async void SendMessageAsync(string message)
        {
            if (_ws.State != WebSocketState.Open)
            {
                throw new Exception("Connection is not open.");
            }

            var messageBuffer = Encoding.UTF8.GetBytes(message);
            var messagesCount = (int)Math.Ceiling((double)messageBuffer.Length / SendChunkSize);

            for (var i = 0; i < messagesCount; i++)
            {
                var offset = (SendChunkSize * i);
                var count = SendChunkSize;
                var lastMessage = ((i + 1) == messagesCount);

                if ((count * (i + 1)) > messageBuffer.Length)
                {
                    count = messageBuffer.Length - offset;
                }

                await _ws.SendAsync(new ArraySegment<byte>(messageBuffer, offset, count), WebSocketMessageType.Text, lastMessage, _cancellationToken);
            }
        }

        //----------------------------------------------------------------------------------------->
        // send binary data to WebSocket server
        //      the actual implementation for the public interface
        //----------------------------------------------------------------------------------------->
        private async void SendBytesAsync(byte[] bytes)
        {
            if (_ws.State != WebSocketState.Open)
            {
                throw new Exception("Connection is not open.");
            }

            var messageBuffer = bytes;
            var messagesCount = (int)Math.Ceiling((double)messageBuffer.Length / SendChunkSize);

            for (var i = 0; i < messagesCount; i++)
            {
                var offset = (SendChunkSize * i);
                var count = SendChunkSize;
                var lastMessage = ((i + 1) == messagesCount);

                if ((count * (i + 1)) > messageBuffer.Length)
                {
                    count = messageBuffer.Length - offset;
                }

                await _ws.SendAsync(new ArraySegment<byte>(bytes, offset, count), WebSocketMessageType.Binary, lastMessage, _cancellationToken);
            }
        }


        //----------------------------------------------------------------------------------------->
        // Connects to WebSocket server
        //      actual implementation for the associated public interface
        //----------------------------------------------------------------------------------------->
        private async void ConnectAsync()
        {
            await _ws.ConnectAsync(_uri, _cancellationToken);
            CallOnConnected();
            StartListen();
        }

        private async void StartListen()
        {
            var buffer = new byte[ReceiveChunkSize];

            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    var stringResult = new StringBuilder();


                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationToken);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await
                                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                            CallOnDisconnected();
                        }
                        else
                        {
                            var str = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            stringResult.Append(str);
                        }

                    } while (!result.EndOfMessage);

                    CallOnMessage(stringResult);
                    await Task.Delay(100);
                }
            }
            catch (Exception)
            {
                CallOnDisconnected();
            }
            finally
            {
                _ws.Dispose();
            }
        }

        private void CallOnMessage(StringBuilder stringResult)
        {
            if (_onMessage != null)
                RunInTask(() => _onMessage(stringResult.ToString(), this));
        }

        private void CallOnDisconnected()
        {
            if (_onDisconnected != null)
                RunInTask(() => _onDisconnected(this));
        }

        private void CallOnConnected()
        {
            if (_onConnected != null)
                RunInTask(() => _onConnected(this));
        }

        private static void RunInTask(Action action)
        {
            Task.Factory.StartNew(action);
        }

        public void SoundShortStream(short[] audio)
        {
            byte[] SoundArray = new byte[1 + audio.Length * 2];
            SoundArray[0] = 2;
            for (int i = 0; i < audio.Length; i++)
            {
                short s = System.Net.IPAddress.HostToNetworkOrder(audio[i]);
                byte[] b = BitConverter.GetBytes(s);
                SoundArray[1 + i * 2] = b[0];
                SoundArray[1 + i * 2 + 1] = b[1];
            }
            SendBytesAsync(SoundArray);
        }

        public void OnWaveInDataAvailable(object sender, WaveInEventArgs e)
        {
            int totalSample = e.BytesRecorded / 2;
            short[] audio = new short[totalSample];
            Buffer.BlockCopy(e.Buffer, 0, audio, 0, e.BytesRecorded);
            SoundShortStream(audio);
        }

    }

    class DemoFEWs
    {

        //--------------------------------------------------------------------->
        // Main: program entrance
        //--------------------------------------------------------------------->
        static void Main(string[] args)
        {
            string url = "ws://localhost:8008";

            int n = 3;

            try 
            {
                WebSocketWrapper wsWrap = WebSocketWrapper.Create(url);

                wsWrap.OnConnect((sock) => {
                    Console.WriteLine("Connected ...");

                }
                );

                wsWrap.OnMessage((msg, sock) => {
                    Console.OutputEncoding = Encoding.UTF8;
                    Console.WriteLine("inmsg: " + msg);

                    if (msg[0].ToString() == "0")
                    {
                        var substring = msg.Substring(1);
                        SessionID sessionid = JsonConvert.DeserializeObject<SessionID>(substring);
                        for (int i = 0; i < n; i++)
                        {
                            //send session information as text message
                            Random rnd = new Random();
                            string textinfo = "{right_text:" + rnd.Next(1, 150).ToString() + ", session_id:\"" + sessionid.session_id + "\", sequence_id:" + rnd.Next(1, 1000).ToString() + "}";
                            Console.WriteLine("Sending Text Message: " + textinfo + " ...");
                            wsWrap.SendMessage(textinfo);


                            //Start sending audio bytes
                            Console.WriteLine("Starting audio streaming ...");
                            WaveInEvent waveIn = new WaveInEvent
                            {
                                DeviceNumber = 1,
                            };
                            waveIn.WaveFormat = new WaveFormat(16000, 16, 1);
                            waveIn.DataAvailable += wsWrap.OnWaveInDataAvailable;
                            waveIn.RecordingStopped += ((sender, e) => { Console.WriteLine("Recording Stopped."); });
                            waveIn.StartRecording();

                            Console.WriteLine("Stream recording Start...");
                            Thread.Sleep(30000);
                            Console.WriteLine("Stream recording Done...");

                            waveIn.StopRecording();
                            waveIn.Dispose();
                            waveIn = null;

                            Thread.Sleep(1000);

                        }
                    }
                }
                );

                

                wsWrap.OnDisconnect((sock) => {
                    Console.WriteLine("Disconnected ...");
                }
                );

                wsWrap.Connect();

                Console.ReadLine();

                Console.ReadLine();

                Console.ReadLine();

                wsWrap.Disconnect();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e);
            }

        }
    }
}
