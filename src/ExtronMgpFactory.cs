using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Factory for the Extron MGP 641 plugin device
	/// </summary>
	public class ExtronMgpFactory : EssentialsPluginDeviceFactory<ExtronMgpDevice>
	{
		/// <summary>
		/// Constructor
		/// </summary>
		public ExtronMgpFactory()
		{
			// The minimum must be numeric (for example 3.0.0), never a prerelease string such as 3.0.0-rc.11;
			// Essentials parses it with System.Version, and a prerelease string silently skips loading the plugin.
			MinimumEssentialsFrameworkVersion = "3.0.0";

			TypeNames = new List<string>() { "extronMgp641", "extronMgp" };
		}

		/// <summary>
		/// Builds and returns an instance of ExtronMgpDevice
		/// </summary>
		public override EssentialsDevice BuildDevice(PepperDash.Essentials.Core.Config.DeviceConfig dc)
		{
			Debug.LogVerbose("[{key}] Factory Attempting to create new device from type: {type}", dc.Key, dc.Type);

			var propertiesConfig = dc.Properties.ToObject<ExtronMgpPropertiesConfig>();
			if (propertiesConfig == null)
			{
				Debug.LogError("[{key}] Factory: failed to read properties config for {name}", dc.Key, dc.Name);
				return null;
			}

			// "ssh", "tcpIp" (Telnet) and "com" (RS-232) are handled by CommFactory
			var comms = CommFactory.CreateCommForDevice(dc);
			if (comms == null)
			{
				Debug.LogError("[{key}] Factory Notice: No control object present for device {name}", dc.Key, dc.Name);
				return null;
			}

			return new ExtronMgpDevice(dc.Key, dc.Name, propertiesConfig, comms);
		}
	}
}
