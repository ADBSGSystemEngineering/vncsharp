// VncSharp - .NET VNC Client Library
// Copyright (C) 2008 David Humphrey
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA

using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.ComponentModel;
using System.Drawing.Imaging;
using log4net;

namespace VncSharp
{
    /// <summary>
    /// Event Handler delegate declaration used by events that signal successful connection with the server.
    /// </summary>
    public delegate void ConnectCompleteHandler(object sender, ConnectEventArgs e);
	
	/// <summary>
	/// When connecting to a VNC Host, a password will sometimes be required.  Therefore a password must be obtained from the user.  A default Password dialog box is included and will be used unless users of the control provide their own Authenticate delegate function for the task.  For example, this might pull a password from a configuration file of some type instead of prompting the user.
	/// </summary>
	public delegate string AuthenticateDelegate();

    public delegate string vncErrorMessage(string Message);

	/// <summary>
	/// SpecialKeys is a list of the various keyboard combinations that overlap with the client-side and make it
	/// difficult to send remotely.  These values are used in conjunction with the SendSpecialKeys method.
	/// </summary>
	public enum SpecialKeys {
		CtrlAltDel,
		AltF4,
		CtrlEsc, 
		Ctrl,
		Alt
	}

    [ToolboxBitmap(typeof(RemoteDesktop), "Resources.vncviewer.ico")]
    /// <summary>
    /// The RemoteDesktop control takes care of all the necessary RFB Protocol and GUI handling, including mouse and keyboard support, as well as requesting and processing screen updates from the remote VNC host.  Most users will choose to use the RemoteDesktop control alone and not use any of the other protocol classes directly.
    /// </summary>
    public class RemoteDesktop : Panel
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        [Description("Raised after a successful call to the Connect() method.")]
        /// <summary>
        /// Raised after a successful call to the Connect() method.  Includes information for updating the local display in ConnectEventArgs.
        /// </summary>
        public event ConnectCompleteHandler ConnectComplete;

        [Description("Raised when the VNC Host drops the connection.")]
        /// <summary>
        /// Raised when the VNC Host drops the connection.
        /// </summary>
        public event EventHandler ConnectionLost;

        [Description("Raised when the VNC Host sends text to the client's clipboard.")]
        /// <summary>
        /// Raised when the VNC Host sends text to the client's clipboard. 
        /// </summary>
        public event EventHandler ClipboardChanged;

        /// <summary>
        /// Points to a Function capable of obtaining a user's password.  By default this means using the PasswordDialog.GetPassword() function; however, users of RemoteDesktop can replace this with any function they like, so long as it matches the delegate type.
        /// </summary>
        public AuthenticateDelegate GetPassword;

        public vncErrorMessage vncMessage;

        Bitmap desktop=null;                          // Internal representation of remote image.
        Image designModeDesktop=null;                 // Used when painting control in VS.NET designer
        Bitmap desktopMessage=null;                   // shows connection info, before desktop is created. (after successfull connection)
        Rectangle desktopMessageRectangle;
        VncClient vnc = null;                           // The Client object handling all protocol-level interaction
        int port = 5900;					     // The port to connect to on remote host (5900 is default)
        System.Net.IPAddress address = new System.Net.IPAddress(new byte[] { 127, 0, 0, 1 });
        string password = "";
        int connectTimeout = 5;
        bool passwordPending = false;            // After Connect() is called, a password might be required.
        bool fullScreenRefresh = false;		     // Whether or not to request the entire remote screen be sent.
        VncDesktopTransformPolicy desktopPolicy=null;
        RuntimeState state = RuntimeState.Disconnected;
        private bool AskForPassword = false;

        BackgroundWorker ConnectBackgroundWorker;

        private enum RuntimeState {
            Disconnected,
            Disconnecting,
            Connected,
            Connecting
        }

        private System.Timers.Timer reconnectTimer = new System.Timers.Timer(5000);
        void reconnectTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // timer is only activated when a connection is active.
            if (log.IsDebugEnabled) log.Debug("reconnect timer: Connect()");
            Connect();
        }

        public RemoteDesktop() : base()
        {
            // The path is not the path of vncsharp.dll, but the path of the container (WinCC graphics, Form, ...).
            // C:/Program Files (x68)/Siemens/WinCC/bin
            System.IO.FileInfo ConfigFile = new System.IO.FileInfo("vnc.log4net.config");
            log4net.Config.XmlConfigurator.Configure(ConfigFile);
            log4net.GlobalContext.Properties["tab"] = '\t';
            if (log.IsInfoEnabled) log.Info("Created new VNC RemoteDesktop Control instance.");

            // Since this control will be updated constantly, and all graphics will be drawn by this class,
            // set the control's painting for best user-drawn performance.
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer |
                     ControlStyles.Selectable |   // BUG FIX (Edward Cooke) -- Adding Control.Select() support
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Opaque,
                     true);

            // Show a screenshot of a Windows desktop from the manifest and cache to be used when painting in design mode
            designModeDesktop = Image.FromStream(Assembly.GetAssembly(GetType()).GetManifestResourceStream("VncSharp.Resources.screenshot.png"));

            // Use a simple desktop policy for design mode.  This will be replaced in Connect()
            desktopPolicy = new VncDesignModeDesktopPolicy(this);
            AutoScroll = desktopPolicy.AutoScroll;
            AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;

            // Users of the control can choose to use their own Authentication GetPassword() method via the delegate above.  This is a default only.
            // TODO: create control property "AskForPassword"
            if (AskForPassword)
            {
                GetPassword = new AuthenticateDelegate(PasswordDialog.GetPassword);
            }
            else
            {
                GetPassword = new AuthenticateDelegate(this.GetPasswordProperty);
            }
            this.reconnectTimer.SynchronizingObject = this; // Elapsed event will run in this thread. No CrossThreadException
            this.reconnectTimer.Elapsed += new System.Timers.ElapsedEventHandler(reconnectTimer_Elapsed);
        }

        #region ControlProperties

        [DefaultValue(5900)]
        [Description("The port number used by the VNC Host (typically 5900)")]
        /// <summary>
        /// The port number used by the VNC Host (typically 5900).
        /// </summary>
        public int VncPort
        {
            get
            {
                return port;
            }
            set
            {
                // Ignore attempts to use invalid port numbers
                if (value < 1 | value > 65535)
                {
                    value = 5900;
                }
                port = value;
                if (value != 5900)
                {
                    if (log.IsInfoEnabled) log.Info("VncPort = " + value);
                }
            }
        }
        [DefaultValue("")]
        [Description("IP address")]
        public string VncAddress
        {
            get
            {
                return address.ToString();
            }
            set
            {
                if (value.Length > 0)
                {
                    if (!System.Net.IPAddress.TryParse(value, out address))
                    {
                        if (log.IsErrorEnabled) log.Error("VncAddress = " + value + ". Not valid Ip address.");
                        DrawDesktopMessage("VncAddress = " + value + " ??");
                    }
                }
            }
        }
        public bool VncConnect
        {
            set
            {
                if (value == true)
                {
                    try
                    {
                        if (log.IsDebugEnabled) log.Debug("VncConnect = true (" + address + ":" + port + ")");
                        Connect(address.ToString());
                    }
                    catch (Exception ex)
                    {
                        if (log.IsErrorEnabled) log.Error("VncConnect = true: " + ex.Message + "\r\n" + ex.StackTrace);
                    }
                }
                else
                {
                    try
                    {
                        switch (state)
                        {
                            case RuntimeState.Connected:
                                if (log.IsInfoEnabled) log.Info("VncConnect = false. Disconnecting " + address + ":" + port);
                                Disconnect();
                                break;
                            case RuntimeState.Connecting:
                            case RuntimeState.Disconnecting:
                            case RuntimeState.Disconnected:
                                if (log.IsDebugEnabled) log.Debug("VncConnect = false.");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (log.IsErrorEnabled) log.Error("VncConnect = false: " + ex.Message + "\r\n" + ex.StackTrace);
                    }
                }
            }
            get
            {
                return false;
            }
        }

        public string VncPassword
        {
            set
            {
                //if (log.IsDebugEnabled) log.Debug("VndPassword; old=" + password + " new=" + value);
                password = value;
            }
            get
            {
                return password;
                /*
                System.Text.StringBuilder showString = new System.Text.StringBuilder();
                for (int i=0;i<password.Length;i++)
                {
                    showString.Append("*");
                }
                return showString.ToString();
                */
            }
        }
        [Description("Timeout for Connect. in Seconds")]
        public int vncConnectTimeout
        {
            set
            {
                if (value >= 1)
                {
                    if (log.IsInfoEnabled) log.Info("vncConnectTimeout changed to "+value);
                    connectTimeout = value;
                }
            }
            get
            {
                return connectTimeout;
            }
        }
        /// <summary>
        /// True if the RemoteDesktop is connected and authenticated (if necessary) with a remote VNC Host; otherwise False.
        /// </summary>
        public bool IsConnected
        {
            get
            {
                return state == RuntimeState.Connected;
            }
        }

        // This is a hack to get around the issue of DesignMode returning
        // false when the control is being removed from a form at design time.
        // First check to see if the control is in DesignMode, then work up 
        // to also check any parent controls.  DesignMode returns False sometimes
        // when it is really True for the parent. Thanks to Claes Bergefall for the idea.

        //protected new bool vncDesignMode

        public bool vncDesignMode
        {
            get
            {
                if (base.DesignMode)
                {
                    return true;
                }
                else
                {
                    Control parent = Parent;

                    while (parent != null)
                    {
                        if (parent.Site != null && parent.Site.DesignMode)
                        {
                            return true;
                        }
                        parent = parent.Parent;
                    }
                    return false;
                }
            }
        }

        private string GetPasswordProperty()
        {
            return this.password;
        }
        /// <summary>
        /// Returns a more appropriate default size for initial drawing of the control at design time
        /// </summary>
        protected override Size DefaultSize {
            get {
                return new Size(400, 200);
            }
        }

        [Description("The name of the remote desktop.")]
        /// <summary>
        /// The name of the remote desktop, or "Disconnected" if not connected.
        /// </summary>
        public string Hostname {
            get {
                return vnc == null ? "Disconnected" : vnc.HostName;
            }
        }

        /// <summary>
        /// The image of the remote desktop.
        /// </summary>
        public Image Desktop {
            get {
                return desktop;
            }
        }

        /*
        //public delegate void VncAlarmDelegate(byte value);
        //public event VncAlarmDelegate VncAlarm;
        private byte mVncAlarm = 0;
        public byte VncAlarms
        {
            set
            {
                if (mVncAlarm != value)
                {
                    mVncAlarm = value;
                    if (VncAlarm != null)
                    {
                        //VncAlarm(value);
                    }
                }
            }
            get { return mVncAlarm; }
        }
        */

        /// <summary>
        /// Get a complete update of the entire screen from the remote host.
        /// </summary>
        /// <remarks>You should allow users to call FullScreenUpdate in order to correct
        /// corruption of the local image.  This will simply request that the next update be
        /// for the full screen, and not a portion of it.  It will not do the update while
        /// blocking.
        /// </remarks>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the Connected state.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void FullScreenUpdate()
        {
            InsureConnection(true);
            fullScreenRefresh = true;
        }

        /// <summary>
        /// Insures the state of the connection to the server, either Connected or Not Connected depending on the value of the connected argument.
        /// </summary>
        /// <param name="connected">True if the connection must be established, otherwise False.</param>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is in the wrong state.</exception>
        private void InsureConnection(bool connected)
        {
            // Grab the name of the calling routine:
            string methodName = new System.Diagnostics.StackTrace().GetFrame(1).GetMethod().Name;

            if (connected)
            {
                /*
                System.Diagnostics.Debug.Assert(state == RuntimeState.Connected ||
                                                state == RuntimeState.Disconnecting, // special case for Disconnect()
                                                string.Format("RemoteDesktop must be in RuntimeState.Connected before calling {0}.", methodName));
                                                */
                if (state != RuntimeState.Connected && state != RuntimeState.Disconnecting)
                {
                    throw new InvalidOperationException("RemoteDesktop must be in Connected state before calling methods that require an established connection.");
                }
            }
            else
            { // disconnected
                /*
                System.Diagnostics.Debug.Assert(state == RuntimeState.Disconnected,
                                                string.Format("RemoteDesktop must be in RuntimeState.Disconnected before calling {0}.", methodName));
                                                */
                if (state != RuntimeState.Disconnected && state != RuntimeState.Disconnecting)
                {
                    throw new InvalidOperationException("RemoteDesktop cannot be in Connected state when calling methods that establish a connection.");
                }
            }
        }

        // This event handler deals with Frambebuffer Updates coming from the host. An
        // EncodedRectangle object is passed via the VncEventArgs (actually an IDesktopUpdater
        // object so that *only* Draw() can be called here--Decode() is done elsewhere).
        // The VncClient object handles thread marshalling onto the UI thread.
        protected void VncUpdate(object sender, VncEventArgs e)
        {
            e.DesktopUpdater.Draw(desktop);
            Invalidate(desktopPolicy.AdjustUpdateRectangle(e.DesktopUpdater.UpdateRectangle));

            if (state == RuntimeState.Connected) {
                vnc.RequestScreenUpdate(fullScreenRefresh);

                // Make sure the next screen update is incremental
                fullScreenRefresh = false;
            }
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host)
        {
            if (state == RuntimeState.Disconnected)
            {
                // Use Display 0 by default.
                Connect(host, 0);
            }
            else
            {
                if (log.IsWarnEnabled) log.Warn("Connect(" + host + ")" + Enum.GetName(typeof(RuntimeState), state));
            }
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, bool viewOnly)
        {
            // Use Display 0 by default.
            Connect(host, 0, viewOnly);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <param name="scaledToFitScreen">Determines whether to use desktop scaling or leave it normal and clip.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, bool viewOnly, bool scaledToFitScreen)
        {
            // Use Display 0 by default.
            Connect(host, 0, viewOnly, scaledToFitScreen);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="display">The Display number (used on Unix hosts).</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, int display)
        {
            Connect(host, display, false);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="display">The Display number (used on Unix hosts).</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, int display, bool viewOnly)
        {
            //Connect(host, display, viewOnly, false);
            Connect(host, display, viewOnly, true);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="display">The Display number (used on Unix hosts).</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <param name="scaledToFitScreen">Determines whether to use desktop scaling or leave it normal and clip.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Connect(string host, int display, bool viewOnly, bool scaledToFitScreen)
        {
            if (log.IsDebugEnabled) log.Debug("Connect("+host+", "+display+", "+viewOnly+", "+scaledToFitScreen+")");
            // TODO: Should this be done asynchronously so as not to block the UI?  Since an event 
            // indicates the end of the connection, maybe that would be a better design.
            if (ConnectBackgroundWorker != null)
            {
                if (ConnectBackgroundWorker.IsBusy)
                {
                    // should wait?
                    if (log.IsErrorEnabled) log.Error("Connect refused. Pending connection not finished yet.");
                    return;
                }
            }

            InsureConnection(false);

            if (host == null)
            {
                if (log.IsErrorEnabled) log.Error("Connect not possible. No host specified!");
                DrawDesktopMessage("Connect not possible. No host specified!");
                throw new ArgumentNullException("host");
            }
            if (display < 0)
            {
                if (log.IsErrorEnabled) log.Error("Connect refused. Display number must be a positive integer.");
                throw new ArgumentOutOfRangeException("display", display, "Display number must be a positive integer.");
            }

            if (log.IsErrorEnabled) log.Error("Connect() DrawDesktopMessage");
            DrawDesktopMessage("Connecting...");
            // Start protocol-level handling and determine whether a password is needed
            if (log.IsErrorEnabled) log.Error("Connect() new VncClient");
            vnc = new VncClient(connectTimeout);
            vnc.ConnectionLost += new EventHandler(VncClientConnectionLost);
            vnc.ServerCutText += new EventHandler(VncServerCutText);

            if (log.IsErrorEnabled) log.Error("Connect() new ConnectBackgroundWorker");
            ConnectBackgroundWorker = new BackgroundWorker();
            ConnectBackgroundWorker.DoWork += (obj, e) => ConnectInBackground(host, display, viewOnly);
            ConnectBackgroundWorker.RunWorkerCompleted += (obj, e) => ConnectCompleted(scaledToFitScreen);
            Connect();
        }

        public void Connect()
        {
            if (this.IsConnected)
            {
                if (log.IsDebugEnabled) log.Debug("RemoteDesktop.Connect() - Connect not needed.");
            }
            else
            {
                // also used to reconnect, when connection has been lost
                if (this.ConnectBackgroundWorker == null)
                {
                    if (log.IsDebugEnabled) log.Debug("RemoteDesktop.Connect() -  ConnectBackgroundWorker null");
                }
                else if (this.ConnectBackgroundWorker.IsBusy)
                {
                    if (log.IsDebugEnabled) log.Debug("RemoteDesktop.Connect() -  ConnectBackgroundWorker null");
                }
                else
                {
                    if (log.IsDebugEnabled) log.Debug("RemoteDesktop.Connect() -  ConnectBackgroundWorker.Run ");
                    ConnectBackgroundWorker.RunWorkerAsync();
                }
            }
        }

        #endregion

        private string ConnectErrorMessage = "";
        private System.Threading.ManualResetEvent ConnectDelay;
        /// <summary>
        /// Connect to a VNC Host on the backgroundworker. Don't access UI objects here.
        /// </summary>
        private void ConnectInBackground(string host, int display, bool viewOnly)
        {
            if (ConnectDelay == null)
            {
                // first time. Don't wait
                ConnectDelay = new System.Threading.ManualResetEvent(false);
            }
            else
            {
                ConnectDelay.WaitOne(1100);
            }
            try
            {
                ConnectErrorMessage = "";
                if (log.IsDebugEnabled) log.Debug("ConnectInBackground(" + host + ":" + VncPort + ")");
                passwordPending = vnc.Connect(host, display, VncPort, viewOnly);
                if (log.IsInfoEnabled) log.Info("Socket Connected. Password required: " + passwordPending);
            }
            catch (Exception ex)
            {
                ConnectErrorMessage = ex.Message;
                if (log.IsErrorEnabled) log.Error("ConnectInBackground: " + ex.Message);
                //this.VncAlarms = 1;
            }
            if ( !this.reconnectTimer.Enabled)
            {
                this.reconnectTimer.Start();
            }
        }
        private void ConnectCompleted(bool scaledToFitScreen)
        {

            if (vnc == null)
            {
                DrawDesktopMessage("ConnectBackgroundWorker.RunWorkerCompleted() vnc = null");
                return;
            }
            if (!vnc.SocketConnected)
            {
                DrawDesktopMessage("ConnectBackgroundWorker.RunWorkerCompleted() vnc not connected.");
                if (ConnectErrorMessage.Length > 0)
                {
                    DrawDesktopMessage(ConnectErrorMessage);
                }
                else
                {
                    DrawDesktopMessage("Not Connected!");
                }
                return;
            }

            SetScalingMode(scaledToFitScreen);
            if (passwordPending)
            {
                // Server needs a password, so call which ever method is refered to by the GetPassword delegate.
                string password = GetPassword();

                if (password == null)
                {
                    // No password could be obtained (e.g., user clicked Cancel), so stop connecting
                    DrawDesktopMessage("No Password supplied!");
                    return;
                }
                else
                {
                    if (log.IsDebugEnabled) log.Debug("ConnectBackgroundWorker.RunWorkerCompleted() - connected, password required -> Athenticate()");
                    DrawDesktopMessage("Connected. Authentication required...");
                    if (Authenticate(password))
                    {
                        if (log.IsDebugEnabled) log.Debug("ConnectBackgroundWorker.RunWorkerCompleted() - connected, password ok -> Initialize()");
                        // Initialize will set the state to 'Connected'
                        Initialize();
                    }
                    else
                    {
                        if (vnc != null)
                        {
                            vnc.Disconnect();
                            //Disconnect();
                        }
                    }
                }
            }
            else
            {
                if (log.IsDebugEnabled) log.Debug("ConnectBackgroundWorker.RunWorkerCompleted() - connected, no password needed. -> Initialize()");
                DrawDesktopMessage("Connected.");
                Initialize();
            }
        }

        /// <summary>
        /// Authenticate with the VNC Host using a user supplied password.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        /// <exception cref="System.NullReferenceException">Thrown if the password is null.</exception>
        /// <param name="password">The user's password.</param>
        public bool Authenticate(string password)
        {
            InsureConnection(false);
            if (!passwordPending)
            {
                if (log.IsErrorEnabled) log.Error("Authentication is only required when Connect() returns True and the VNC Host requires a password.");
                DrawDesktopMessage("Authentication is only required when Connect() returns True and the VNC Host requires a password.");
                //throw new InvalidOperationException("Authentication is only required when Connect() returns True and the VNC Host requires a password.");
                return false;
            }
            else if (password == null)
            {
                if (log.IsErrorEnabled) log.Error("Authentication failed. No password supplied.");
                DrawDesktopMessage("Authentication failed. No password supplied.");
                //throw new NullReferenceException("password");
                return false;
            }
            else if (password.Length == 0)
            {
                passwordPending = false;  // repeated calls to Authenticate should fail.
                if (log.IsErrorEnabled) log.Error("Authentication failed. No password supplied.");
                DrawDesktopMessage("Authentication failed. No password supplied.");
                //throw new NullReferenceException("password");
                return false;
            }

            try
            {
                if (vnc.Authenticate(password))
                {
                    if (log.IsErrorEnabled) log.Error("Authentication successfull. -> Initialize().");
                    //Initialize();

                    return true;
                }
                else
                {
                    if (log.IsErrorEnabled) log.Error("Authentication failed. Verify password.");
                    DrawDesktopMessage("Authentication failed. Verify password.");
                }
            }
            catch (Exception ex)
            {
                if (log.IsErrorEnabled) log.Error("Authentication failed. " + ex.Message);
                DrawDesktopMessage("Authentication failed. ");
            }
            //OnConnectionLost();
            return false;
        }

        /// <summary>
        /// Changes the input mode to view-only or interactive.
        /// </summary>
        /// <param name="viewOnly">True if view-only mode is desired (no mouse/keyboard events will be sent).</param>
        public void SetInputMode(bool viewOnly)
        {
            vnc.SetInputMode(viewOnly);
        }

        /// <summary>
        /// Set the remote desktop's scaling mode.
        /// </summary>
        /// <param name="scaledToFitScreen">Determines whether to use desktop scaling or leave it normal and clip.</param>
        public void SetScalingMode(bool scaledToFitScreen)
        {
            if (scaledToFitScreen)
            {
                desktopPolicy = new VncScaledDesktopPolicy(vnc, this);
                AutoScroll = false;
            }
            else
            {
                desktopPolicy = new VncClippedDesktopPolicy(vnc, this);
                AutoScroll = desktopPolicy.AutoScroll;
                AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;
            }

            Invalidate();
        }

        /// <summary>
        /// After protocol-level initialization and connecting is complete, the local GUI objects have to be set-up, and requests for updates to the remote host begun.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already in the Connected state.  See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>		
        protected void Initialize()
        {
            // Finish protocol handshake with host now that authentication is done.
            InsureConnection(false);
            vnc.Initialize();
            SetMouseCursor(RuntimeState.Connected);
            //this.VncAlarms = 0;
            // Create a buffer on which updated rectangles will be drawn and draw a "please wait..." 
            // message on the buffer for initial display until we start getting rectangles
            SetupDesktop();

            // Tell the user of this control the necessary info about the desktop in order to setup the display
            OnConnectComplete(new ConnectEventArgs(vnc.Framebuffer.Width,
                                                   vnc.Framebuffer.Height,
                                                   vnc.Framebuffer.DesktopName));

            // Refresh scroll properties
            //if (log.IsDebugEnabled) log.Debug("AutoScrollMinSize "+ AutoScrollMinSize+" -> "+ desktopPolicy.AutoScrollMinSize);
            AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;

            // Start getting updates from the remote host (vnc.StartUpdates will begin a worker thread).
            vnc.VncUpdate += new VncUpdateHandler(VncUpdate);
            vnc.StartUpdates();
        }

        private void SetMouseCursor(RuntimeState newState)
        {
            state = newState;

            // Set mouse pointer according to new state
            switch (state)
            {
                case RuntimeState.Connected:
                    // Change the cursor to the "vnc" custor--a see-through dot
                    Cursor = new Cursor(GetType(), "Resources.vnccursor.cur");
                    break;
                // All other states should use the normal cursor.
                case RuntimeState.Disconnected:
                default:
                    Cursor = Cursors.Default;
                    break;
            }
        }

        /// <summary>
        /// Creates and initially sets-up the local bitmap that will represent the remote desktop image.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not already in the Connected state. See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        protected void SetupDesktop()
        {
            InsureConnection(true);

            // Create a new bitmap to cache locally the remote desktop image.  Use the geometry of the
            // remote framebuffer, and 32bpp pixel format (doesn't matter what the server is sending--8,16,
            // or 32--we always draw 32bpp here for efficiency).
            if (log.IsDebugEnabled)
            {
                if (vnc == null)
                    log.Debug("SetupDesktop vnc == null");
                else if (vnc.Framebuffer == null)
                    log.Debug("SetupDesktop Framebuffer == null");
                else
                    log.Debug("SetupDesktop "+ vnc.Framebuffer.Width+" " + vnc.Framebuffer.Height);
            }
            if ((vnc.Framebuffer.Width == 0) || (vnc.Framebuffer.Height == 0))
            {
                DrawDesktopMessage("SetupDesktop Received invalid size from host.");
            }
            try
            {
                desktop = new Bitmap(vnc.Framebuffer.Width, vnc.Framebuffer.Height, PixelFormat.Format32bppPArgb);
                // Draw a "please wait..." message on the local desktop until the first
                // rectangle(s) arrive and overwrite with the desktop image.
                DrawDesktopMessage("Connecting to VNC host,  please wait...");
            }
            catch (Exception ex)
            {
                DrawDesktopMessage("SetupDesktop " + ex.Message);
            }
        }

        /// <summary>
        /// Draws the given message (white text) on the local desktop (all black).
        /// </summary>
        /// <param name="message">The message to be drawn.</param>
        protected void DrawDesktopMessage(string message)
        {
            //System.Diagnostics.Debug.Assert(desktop != null, "Can't draw on desktop when null.");
            if ((desktop == null) || (state== RuntimeState.Disconnected))
            {
                desktopMessage = new Bitmap(base.Width, base.Height, PixelFormat.Format32bppPArgb);
                desktopMessageRectangle = new Rectangle(0, desktopMessage.Height / 2, desktopMessage.Width, 24);
                try
                {
                    using (Graphics g = Graphics.FromImage(desktopMessage))
                    {
                        g.FillRectangle(Brushes.Black, desktopMessageRectangle);
                        StringFormat format = new StringFormat();
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        g.DrawString(message, new Font("Arial", 12), new SolidBrush(Color.White), desktopMessageRectangle, format);
                    }
                    this.Invalidate();
                }
                catch
                {
                    if (log.IsErrorEnabled) log.Error("DrawDesktopMessage " + message);
                }
            }
            else
            {
                // Draw the given message on the local desktop
                using (Graphics g = Graphics.FromImage(desktop))
                {
                    g.FillRectangle(Brushes.Black, vnc.Framebuffer.Rectangle);

                    StringFormat format = new StringFormat();
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;

                    g.DrawString(message,
                                 new Font("Arial", 12),
                                 new SolidBrush(Color.White),
                                 new PointF(vnc.Framebuffer.Width / 2, vnc.Framebuffer.Height / 2), format);
                }
            }
        }

        /// <summary>
        /// Stops the remote host from sending further updates and disconnects.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not already in the Connected state. See <see cref="VncSharp.RemoteDesktop.IsConnected" />.</exception>
        public void Disconnect()
		{
            // Called by: vnc.OnConnectionLost event
			InsureConnection(true);
			vnc.Disconnect();
			SetMouseCursor(RuntimeState.Disconnected);
            // generate the RemoteDesktop.OnConnectionLost event on the Control
			OnConnectionLost();
            DrawDesktopMessage("Disconnected.");
			Invalidate();
		}

        /// <summary>
        /// Fills the remote server's clipboard with the text in the client's clipboard, if any.
        /// </summary>
        public void FillServerClipboard()
        {
            FillServerClipboard(Clipboard.GetText());
        }

        /// <summary>
        /// Fills the remote server's clipboard with text.
        /// </summary>
        /// <param name="text">The text to put in the server's clipboard.</param>
        public void FillServerClipboard(string text)
        {
            vnc.WriteClientCutText(text);
        }

		protected override void Dispose(bool disposing)
		{
            if (vnc != null)
            {
                try {vnc.ConnectionLost -= new EventHandler(VncClientConnectionLost);} catch { }
                try {vnc.ServerCutText -= new EventHandler(VncServerCutText); } catch { }                
            }
            if (RfbProtocol.CancelConnecting != null)
            {
                RfbProtocol.CancelConnecting.Set();
            }
			if (disposing)
            {
				// Make sure the connection is closed--should never happen :)
				if (state != RuntimeState.Disconnected) 
                {
					Disconnect();
				}

				// See if either of the bitmaps used need clean-up.  
				if (desktop != null) desktop.Dispose();
                if (desktopMessage != null) desktopMessage.Dispose();
				if (designModeDesktop != null) designModeDesktop.Dispose();
			}
			base.Dispose(disposing);
		}

		protected override void OnPaint(PaintEventArgs pe)
		{
            // If the control is in design mode, draw a nice background, otherwise paint the desktop.
            if (vncDesignMode)
            {
                //if (log.IsDebugEnabled) log.Debug("OnPaint vncDesignMode");
                // Draw a static screenshot of a Windows desktop to simulate the control in action
                //System.Diagnostics.Debug.Assert(designModeDesktop != null);
                DrawDesktopImage(designModeDesktop, pe.Graphics);
            }
            else
            {
                if (desktop != null)
                {
                    switch (state)
                    {
                        case RuntimeState.Connected:
                            if (log.IsDebugEnabled) log.Debug("OnPaint Connected");
                            //System.Diagnostics.Debug.Assert(desktop != null);
                            DrawDesktopImage(desktop, pe.Graphics);
                            break;
                        case RuntimeState.Disconnected:
                            if (log.IsDebugEnabled) log.Debug("OnPaint Disconnected");
                            // Do nothing, just black background.
                            if (desktopMessage != null)
                            {
                                DrawDesktopImage(desktopMessage, pe.Graphics);
                            }
                            break;
                        default:
                            if (log.IsDebugEnabled) log.Debug("OnPaint Connecting/Disconnecting");
                            // Sanity check
                            //throw new NotImplementedException(string.Format("RemoteDesktop in unknown State: {0}.", state.ToString()));
                            break;
                    }
                }
                else if (desktopMessage != null)
                {
                    try
                    {
                        if (log.IsErrorEnabled) log.Error("OnPaint desktopMessage -> DrawDesktopImage() ");
                        DrawDesktopImage(desktopMessage, pe.Graphics);
                    }
                    catch (Exception ex)
                    {
                        if (log.IsErrorEnabled) log.Error("OnPaint desktopMessage: "+ex.Message);
                    }
                }
            }
            base.OnPaint(pe);
		}

        protected override void OnResize(EventArgs eventargs)
        {
            // Fix a bug with a ghost scrollbar in clipped mode on maximize
            Control parent = Parent;
            while (parent != null)
            {
                if (parent is Form)
                {
                    Form form = parent as Form;
                    if (form.WindowState == FormWindowState.Maximized)
                        form.Invalidate();
                    parent = null;
                }
                else
                {
                    parent = parent.Parent;
                }
            }
            base.OnResize(eventargs);
        }

        /// <summary>
        /// Draws an image onto the control in a size-aware way.
        /// </summary>
        /// <param name="desktopImage">The desktop image to be drawn to the control's sufrace.</param>
        /// <param name="g">The Graphics object representing the control's drawable surface.</param>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not already in the Connected state.</exception>
        protected void DrawDesktopImage(Image desktopImage, Graphics g)
        {
            Rectangle rect = desktopPolicy.RepositionImage(desktopImage);
            /*
            if (log.IsDebugEnabled)
            {
                log.Debug("DrawDesktopImage Width=" + desktopImage.Width + " Height=" + desktopImage.Height
                + " Rectangle X=" + rect.X + " Y=" + rect.Y + " Width=" + rect.Width + " Height=" + rect.Height);
                log.Debug("RemoteDesktop ClientSize Width=" + this.ClientSize.Width + " Height=" + this.ClientSize.Height);
                log.Debug("RemoteDesktop ClientRectangle X=" + this.ClientRectangle.X + " Y=" + this.ClientRectangle.Y + " Width=" + this.ClientRectangle.Width + " Height=" + this.ClientRectangle.Height);
            }
            */
            try
            {
                g.DrawImage(desktopImage, rect);
            }
            catch
            {
                if (log.IsErrorEnabled)
                {
                    log.Error("DrawDesktopImage Width=" + desktopImage.Width + " Height=" + desktopImage.Height
                    + " Rectangle X=" + rect.X + " Y=" + rect.Y + " Width=" + rect.Width + " Height=" + rect.Height);
                    log.Debug("RemoteDesktop ClientSize Width=" + this.ClientSize.Width + " Height=" + this.ClientSize.Height);
                    log.Debug("RemoteDesktop ClientRectangle X=" + this.ClientRectangle.X + " Y=" + this.ClientRectangle.Y + " Width=" + this.ClientRectangle.Width + " Height=" + this.ClientRectangle.Height);
                }

            }
        }

		/// <summary>
		/// RemoteDesktop listens for ConnectionLost events from the VncClient object.
		/// </summary>
		/// <param name="sender">The VncClient object that raised the event.</param>
		/// <param name="e">An empty EventArgs object.</param>
		protected void VncClientConnectionLost(object sender, EventArgs e)
		{
			// If the remote host dies, and there are attempts to write
			// keyboard/mouse/update notifications, this may get called 
			// many times, and from main or worker thread.
			// Guard against this and invoke Disconnect once.
			if (state == RuntimeState.Connected)
            {
                SetMouseCursor(RuntimeState.Disconnecting);
				Disconnect();
            }
            this.reconnectTimer.Start();
        }

        // Handle the VncClient ServerCutText event and bubble it up as ClipboardChanged.
        protected void VncServerCutText(object sender, EventArgs e)
        {
            OnClipboardChanged();
        }

        protected void OnClipboardChanged()
        {
            if (ClipboardChanged != null)
                ClipboardChanged(this, EventArgs.Empty);
        }

		/// <summary>
		/// Dispatches the ConnectionLost event if any targets have registered.
		/// </summary>
		/// <param name="e">An EventArgs object.</param>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is in the Connected state.</exception>
		protected void OnConnectionLost()
		{
			if (ConnectionLost != null)
            {
				ConnectionLost(this, EventArgs.Empty);
			}
		}
		
		/// <summary>
		/// Dispatches the ConnectComplete event if any targets have registered.
		/// </summary>
		/// <param name="e">A ConnectEventArgs object with information about the remote framebuffer's geometry.</param>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the Connected state.</exception>
		protected void OnConnectComplete(ConnectEventArgs e)
		{
            this.reconnectTimer.Stop();
			if (ConnectComplete != null) {
				ConnectComplete(this, e);
			}
		}

		// Handle Mouse Events:		 -------------------------------------------
		// In all cases, the same thing needs to happen: figure out where the cursor
		// is, and then figure out the state of all mouse buttons.
		// TODO: currently we don't handle the case of 3-button emulation with 2-buttons.
		protected override void OnMouseMove(MouseEventArgs mea)
		{
			// Only bother if the control is connected.
			if (IsConnected) {
				// See if the mouse pointer is inside the area occupied by the desktop on screen.
                Rectangle adjusted = desktopPolicy.GetMouseMoveRectangle();
				if (adjusted.Contains(PointToClient(MousePosition)))
					UpdateRemotePointer();
			}
			base.OnMouseMove(mea);
		}

		protected override void OnMouseDown(MouseEventArgs mea)
		{
            // BUG FIX (Edward Cooke) -- Deal with Control.Select() semantics
            if (!Focused) {
                Focus();
                Select();
            } else {
                UpdateRemotePointer();
            }
			base.OnMouseDown(mea);
		}
		
		// Find out the proper masks for Mouse Button Up Events
		protected override void OnMouseUp(MouseEventArgs mea)
		{
   			UpdateRemotePointer();
			base.OnMouseUp(mea);
		}
		
		// TODO: Perhaps overload UpdateRemotePointer to take a flag indicating if mousescroll has occured??
		protected override void OnMouseWheel(MouseEventArgs mea)
		{
			// HACK: this check insures that while in DesignMode, no messages are sent to a VNC Host
			// (i.e., there won't be one--NullReferenceException)			
            if (!vncDesignMode && IsConnected) {
				Point current = PointToClient(MousePosition);
				byte mask = 0;

				// mouse was scrolled forward
				if (mea.Delta > 0) {
					mask += 8;
				} else if (mea.Delta < 0) { // mouse was scrolled backwards
					mask += 16;
				}

				vnc.WritePointerEvent(mask, desktopPolicy.GetMouseMovePoint(current));
			}			
			base.OnMouseWheel(mea);
		}
		
		private void UpdateRemotePointer()
		{
			// HACK: this check insures that while in DesignMode, no messages are sent to a VNC Host
			// (i.e., there won't be one--NullReferenceException)			
			if (!vncDesignMode && IsConnected) {
				Point current = PointToClient(MousePosition);
				byte mask = 0;

				if (Control.MouseButtons == MouseButtons.Left)   mask += 1;
				if (Control.MouseButtons == MouseButtons.Middle) mask += 2;
				if (Control.MouseButtons == MouseButtons.Right)  mask += 4;

                Point adjusted = desktopPolicy.UpdateRemotePointer(current);
                //if (adjusted.X < 0 || adjusted.Y < 0)
                //    throw new Exception();
                if ((adjusted.X >= 0) && (adjusted.Y >= 0))
                {
                    vnc.WritePointerEvent(mask, desktopPolicy.UpdateRemotePointer(current));
                }
            }
		}

		// Handle Keyboard Events:		 -------------------------------------------
		// These keys don't normally throw an OnKeyDown event. Returning true here fixes this.
		protected override bool IsInputKey(Keys keyData)
		{
			switch (keyData) {
				case Keys.Tab:
				case Keys.Up:
				case Keys.Down:
				case Keys.Left:
				case Keys.Right:
				case Keys.Shift:
				case Keys.RWin:
				case Keys.LWin:
					return true;
				default:
					return base.IsInputKey(keyData);
			}
		}

		// Thanks to Lionel Cuir, Christian and the other developers at 
		// Aulofee.com for cleaning-up my keyboard code, specifically:
		// ManageKeyDownAndKeyUp, OnKeyPress, OnKeyUp, OnKeyDown.
		private void ManageKeyDownAndKeyUp(KeyEventArgs e, bool isDown)
		{
		    UInt32 keyChar;
		    bool isProcessed = true;
		    switch(e.KeyCode)
		    {
			    case Keys.Tab:				keyChar = 0x0000FF09;		break;
			    case Keys.Enter:			keyChar = 0x0000FF0D;		break;
			    case Keys.Escape:			keyChar = 0x0000FF1B;		break;
			    case Keys.Home:				keyChar = 0x0000FF50;		break;
			    case Keys.Left:				keyChar = 0x0000FF51;		break;
			    case Keys.Up:				keyChar = 0x0000FF52;		break;
			    case Keys.Right:			keyChar = 0x0000FF53;		break;
			    case Keys.Down:				keyChar = 0x0000FF54;		break;
			    case Keys.PageUp:			keyChar = 0x0000FF55;		break;
			    case Keys.PageDown:			keyChar = 0x0000FF56;		break;
			    case Keys.End:				keyChar = 0x0000FF57;		break;
			    case Keys.Insert:			keyChar = 0x0000FF63;		break;
			    case Keys.ShiftKey:			keyChar = 0x0000FFE1;		break;

                // BUG FIX -- added proper Alt/CTRL support (Edward Cooke)
                case Keys.Alt:              keyChar = 0x0000FFE9;       break;
                case Keys.ControlKey:       keyChar = 0x0000FFE3;       break;
                case Keys.LControlKey:      keyChar = 0x0000FFE3;       break;
                case Keys.RControlKey:      keyChar = 0x0000FFE4;       break;
			
			    case Keys.Menu:				keyChar = 0x0000FFE9;		break;
			    case Keys.Delete:			keyChar = 0x0000FFFF;		break;
			    case Keys.LWin:				keyChar = 0x0000FFEB;		break;
			    case Keys.RWin:				keyChar = 0x0000FFEC;		break;
			    case Keys.Apps:				keyChar = 0x0000FFEE;		break;
			    case Keys.F1:
			    case Keys.F2:
			    case Keys.F3:
			    case Keys.F4:
			    case Keys.F5:
			    case Keys.F6:
			    case Keys.F7:
			    case Keys.F8:
			    case Keys.F9:
			    case Keys.F10:
			    case Keys.F11:
			    case Keys.F12:
				    keyChar = 0x0000FFBE + ((UInt32)e.KeyCode - (UInt32)Keys.F1);
				    break;
			    default:
				    keyChar = 0;
				    isProcessed = false;
				    break;
		    }

		    if(isProcessed)
		    {
			    vnc.WriteKeyboardEvent(keyChar, isDown);
			    e.Handled = true;
		    }
		}

		// HACK: the following overrides do a double check on DesignMode so 
		// that if still in design mode, no messages are sent for 
		// mouse/keyboard events (i.e., there won't be Host yet--
		// NullReferenceException)			
		protected override void OnKeyPress(KeyPressEventArgs e)
		{
			base.OnKeyPress (e);
		    if (vncDesignMode || !IsConnected)
			    return;
			
		    if (e.Handled)
			    return;
	
		    if(Char.IsLetterOrDigit(e.KeyChar) || Char.IsWhiteSpace(e.KeyChar) || Char.IsPunctuation(e.KeyChar) ||
			    e.KeyChar == '~' || e.KeyChar == '`' || e.KeyChar == '<' || e.KeyChar == '>' ||
			    e.KeyChar == '|' || e.KeyChar == '=' || e.KeyChar == '+' || e.KeyChar == '$' || e.KeyChar == '^')
		    {
			    vnc.WriteKeyboardEvent((UInt32)e.KeyChar, true);
			    vnc.WriteKeyboardEvent((UInt32)e.KeyChar, false);
		    }
		    else if(e.KeyChar == '\b')
		    {
			    UInt32 keyChar = ((UInt32)'\b') | 0x0000FF00;
			    vnc.WriteKeyboardEvent(keyChar, true);
			    vnc.WriteKeyboardEvent(keyChar, false);
		    }
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
            if (vncDesignMode || !IsConnected)
				return;

			ManageKeyDownAndKeyUp(e, true);
			if(e.Handled)
				return;

			base.OnKeyDown(e);
		}

		protected override void OnKeyUp(KeyEventArgs e)
		{
            if (vncDesignMode || !IsConnected)
				return;

			ManageKeyDownAndKeyUp(e, false);
			if (e.Handled)
				return;

			base.OnKeyDown(e);
		}

		/// <summary>
		/// Sends a keyboard combination that would otherwise be reserved for the client PC.
		/// </summary>
		/// <param name="keys">SpecialKeys is an enumerated list of supported keyboard combinations.</param>
		/// <remarks>Keyboard combinations are Pressed and then Released, while single keys (e.g., SpecialKeys.Ctrl) are only pressed so that subsequent keys will be modified.</remarks>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the Connected state.</exception>
		public void SendSpecialKeys(SpecialKeys keys)
		{
			this.SendSpecialKeys(keys, true);
		}

		/// <summary>
		/// Sends a keyboard combination that would otherwise be reserved for the client PC.
		/// </summary>
		/// <param name="keys">SpecialKeys is an enumerated list of supported keyboard combinations.</param>
		/// <remarks>Keyboard combinations are Pressed and then Released, while single keys (e.g., SpecialKeys.Ctrl) are only pressed so that subsequent keys will be modified.</remarks>
		/// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the Connected state.</exception>
		public void SendSpecialKeys(SpecialKeys keys, bool release)
		{
			InsureConnection(true);
			// For all of these I am sending the key presses manually instead of calling
			// the keyboard event handlers, as I don't want to propegate the calls up to the 
			// base control class and form.
			switch(keys) {
				case SpecialKeys.Ctrl:
					PressKeys(new uint[] { 0xffe3 }, release);	// CTRL, but don't release
					break;
				case SpecialKeys.Alt:
					PressKeys(new uint[] { 0xffe9 }, release);	// ALT, but don't release
					break;
				case SpecialKeys.CtrlAltDel:
					PressKeys(new uint[] { 0xffe3, 0xffe9, 0xffff }, release); // CTRL, ALT, DEL
					break;
				case SpecialKeys.AltF4:
					PressKeys(new uint[] { 0xffe9, 0xffc1 }, release); // ALT, F4
					break;					
				case SpecialKeys.CtrlEsc:
					PressKeys(new uint[] { 0xffe3, 0xff1b }, release); // CTRL, ESC
					break;
				// TODO: are there more I should support???
				default:
					break;
			}
		}
		
		/// <summary>
		/// Given a list of keysym values, sends a key press for each, then a release.
		/// </summary>
		/// <param name="keys">An array of keysym values representing keys to press/release.</param>
		/// <param name="release">A boolean indicating whether the keys should be Pressed and then Released.</param>
		private void PressKeys(uint[] keys, bool release)
		{
			//System.Diagnostics.Debug.Assert(keys != null, "keys[] cannot be null.");
            if (keys != null)
            {
                for (int i = 0; i < keys.Length; ++i)
                {
                    vnc.WriteKeyboardEvent(keys[i], true);
                }
                if (release)
                {
                    // Walk the keys array backwards in order to release keys in correct order
                    for (int i = keys.Length - 1; i >= 0; --i)
                    {
                        vnc.WriteKeyboardEvent(keys[i], false);
                    }
                }
            }
		}
	}
}