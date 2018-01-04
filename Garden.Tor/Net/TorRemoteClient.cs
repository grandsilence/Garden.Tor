using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Garden.Tor.Net
{
    internal class TorRemoteClient : TelnetClient
    {
        /// <summary>
        /// Authenticate on the Tor remote server.
        /// </summary>
        /// <param name="password">Your password</param>
        /// <returns>Returns true if successfuly authenticated</returns>
        public bool Authenticate(string password)
        {
            string escapedPassword = password.Replace("\"", "\\\"");
            WriteLine($"AUTHENTICATE \"{escapedPassword}\"");
            return ResponseContains("250");
        }

        public void ListenEvents() => WriteLine("SETEVENTS SIGNAL");
        
        public void ExitOnDisconnect() => WriteLine("TAKEOWNERSHIP");

        public bool CircuitEstablished()
        {
            WriteLine("getinfo status/circuit-established");
            return ResponseContains("established=1");
        }
        
        protected override void Dispose(bool disposing)
        {
            if (Disposed || !disposing) 
                return;

            WriteLine("QUIT");
            base.Dispose(true);
        }
    }
}
