using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Garden.Tor.Net
{
    internal class TorRemoteClient : TelnetClient
    {
        protected TorRemoteClient()
        {
            // Soft exit for Tor
            BeforeClose += (sender, args) => WriteLine("QUIT");
        }

        /// <summary>
        /// Authenticate on the Tor remote server.
        /// </summary>
        /// <param name="password">Your password</param>
        /// <returns>Returns true if successfuly authenticated</returns>
        public bool Authenticate(string password)
        {
            string escapedPassword = password.Replace("\"", "\\\"");
            WriteLine($"AUTHENTICATE \"{escapedPassword}\"");
            if (!ResponseContains("250"))
                return false;

            WriteLine("SETEVENTS SIGNAL"); // Listen Events
            WriteLine("TAKEOWNERSHIP"); // Exit On Disconnect
            return true;
        }

        public bool CircuitEstablished()
        {
            WriteLine("getinfo status/circuit-established");
            return ResponseContains("established=1");
        }
    }
}
