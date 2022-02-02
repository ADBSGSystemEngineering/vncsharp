using System.Reflection;
using System.Runtime.CompilerServices;
using log4net;

[assembly: AssemblyTitle("VncSharp")]
[assembly: AssemblyDescription(".NET VNC Client control")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("David Humphrey")]
[assembly: AssemblyProduct("")]
[assembly: AssemblyCopyright("GPL 2")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]		

[assembly: AssemblyVersion("1.0.11")]

[assembly: log4net.Config.XmlConfigurator(ConfigFile = "vnc.log4net.config", Watch = true)]
