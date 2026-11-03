using System.Collections.Generic;
using PepperDash.Core;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Config;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Factory for <see cref="ExtronMgpMock"/>, the in-memory bench-test stand-in for an MGP 641.  Swap a real device to
	/// the mock by changing its config <c>type</c> to <c>extronMgp641Mock</c>.  Never touches <c>control</c> or CommFactory.
	/// </summary>
	public class ExtronMgpMockFactory : EssentialsPluginDeviceFactory<ExtronMgpMock>
	{
		/// <summary>Constructor</summary>
		public ExtronMgpMockFactory()
		{
			MinimumEssentialsFrameworkVersion = "3.0.0";

			TypeNames = new List<string>() { "extronMgp641Mock", "extronMgpMock" };
		}

		/// <inheritdoc />
		public override EssentialsDevice BuildDevice(DeviceConfig dc)
		{
			Debug.LogVerbose("[{key}] Factory Attempting to create new device from type: {type}", dc.Key, dc.Type);

			var properties = dc.Properties.ToObject<ExtronMgpMockProperties>();
			if (properties == null)
			{
				Debug.LogError("[{key}] Factory: failed to read properties config for {name}", dc.Key, dc.Name);
				return null;
			}

			return new ExtronMgpMock(dc.Key, dc.Name, properties);
		}
	}
}
