using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Garden.Tor.Net
{
    internal abstract class TelnetClient : IDisposable
    {
        private readonly TcpClient _socket = new TcpClient();
        private readonly NetworkStream _socketStream;

        public bool IsConnected => _socket.Connected;
        public string HostName { get; private set; }
        public int Port { get; private set; }

        // Events
        public event EventHandler BeforeClose;

        protected TelnetClient()
        {
            _socketStream = _socket.GetStream();
        }

        /// <summary>
        /// Connects to the telnet server.
        /// </summary>
        /// <param name="port">Telnet port number</param>
        /// <param name="hostName">Host name</param>
        public void Connect(int port, string hostName = "127.0.0.1")
        {
            _socket.Connect(hostName, port);
            if (_socket.Connected)
            {
                HostName = hostName;
                Port = port;
            }
            else
            {
                HostName = null;
                Port = 0;
            }
        }

        /// <summary>
        /// Send telnet text command to the server.
        /// </summary>
        /// <param name="cmd">Command text</param>
        protected void Write(string cmd)
        {
            if (!_socket.Connected)
                return;

            // Escape command
            byte[] buf = Encoding.ASCII.GetBytes(cmd.Replace("\0xFF", "\0xFF\0xFF"));
            _socketStream.Write(buf, 0, buf.Length);
        }

        /// <summary>
        /// Send telnet text command with new line to the server.
        /// </summary>
        /// <param name="cmd">Command text</param>
        protected void WriteLine(string cmd) 
        {
            Write(cmd + "\n");
        }

        /// <summary>
        /// Reads the output to string.
        /// </summary>
        /// <returns>Response as string</returns>
        protected string Read()
        {
            if (!_socket.Connected)
                return null;

            var sb = new StringBuilder();
            do {
                Parse(sb);
                // TODO: bypass sleep
                Thread.Sleep(100); // Delay is required
            } while (_socket.Available > 0);

            return sb.ToString();
        }

        /// <summary>
        /// Reads the output and looks for the occurrence of the keyword.
        /// </summary>
        /// <param name="keyword"></param>
        /// <returns>Returns true if response contains the keyword</returns>
        protected bool ResponseContains(string keyword) => Read().Contains(keyword);

        #region Command Parsing
        private enum Verb
        {
            StreamEnd = -1,
            OptionSga = 3,
            Will = 251,
            Wont = 252,
            Do = 253,
            Dont = 254,
            IsACommnand = 255
        }
        
        private Verb ReadVerb() => (Verb)_socketStream.ReadByte();
        
        private void WriteVerb(Verb verb) => _socketStream.WriteByte((byte)verb);

        private void Parse(StringBuilder sb) 
        {
            while (_socket.Available > 0) {
                var verb = ReadVerb();
                if (verb == Verb.StreamEnd)
                    continue;

                // Append raw byte if not a command
                if (verb != Verb.IsACommnand)
                {
                    sb.Append((char)verb);
                    continue;
                }

                // Command parser
                verb = ReadVerb();
                switch (verb)
                {
                    case Verb.IsACommnand:
                        // Ansi 255 symbol escape - not a command
                        sb.Append((char)verb);
                        continue;
                    case Verb.Will:
                    case Verb.Wont:
                    case Verb.Do:
                    case Verb.Dont:
                        // reply to all commands with "WONT", unless it is SGA (suppres go ahead)
                        var option = ReadVerb();
                        if (option == Verb.StreamEnd) 
                            continue;
                        
                        // Write body of the command
                        WriteVerb(Verb.IsACommnand);
                        
                        // Write action
                        var verbAction = option == Verb.OptionSga
                            ? (verb == Verb.Do ? Verb.Will : Verb.Do)
                            : (verb == Verb.Do ? Verb.Wont : Verb.Dont);
                        WriteVerb(verbAction);
                        
                        // Write arguments
                        WriteVerb(option);
                        break;
                }
            }
        }
        #endregion

        #region IDisposable Support Pattern
        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) 
                return;

            // Release managed objects
            if (disposing)
            {
                BeforeClose?.Invoke(null, null);
                _socket?.Dispose();
            }

            // Set big fields to NULL here:
            _disposed = true;
        }
        
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}