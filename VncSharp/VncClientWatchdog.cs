using System;
using System.Threading;
using log4net;

namespace VncSharp
{
    public class VncClientWatchdog
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private int RfbUpdatesReceived = 1;

        public bool SendLifeSign
        {
            // called by watchdog. Every time the watchdog requests if lifesign should be send, the counter is reset to 0.
            get
            {
                if (this.RfbUpdatesReceived == 0)
                {
                    // should not stay zero
                    return true;
                }
                else
                {
                    if (log.IsDebugEnabled) log.Debug("RequestLifeSign not necessary " + this.RfbUpdatesReceived);
                    Interlocked.Exchange(ref this.RfbUpdatesReceived, (int)0);
                    return false;
                }
            }
        }

        public bool DataReceived
        {
            set
            {
                if (this.RfbUpdatesReceived < int.MaxValue)
                {
                    Interlocked.Increment(ref this.RfbUpdatesReceived);
                }
            }
        }
    }
}
