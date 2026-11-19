using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Routing input ports of the MGP 641, shared by the real device and the mock.
	/// </summary>
	/// <remarks>
	/// The MGP does no input switching of its own: HDMI input N feeds window N, and the sixth HDMI input is the
	/// unscaled background.  Sources are routed upstream (DM, NVX, ...) and tied to these ports in the Essentials
	/// tie-line config, e.g. <c>"sourceKey": "dmMd8x8-1", "sourcePortKey": "dmOut1", "destinationKey": "mgp-01",
	/// "destinationPortKey": "hdmiIn1"</c>.  Ports are Audio | Video HDMI: the unit de-embeds audio from its inputs.
	/// </remarks>
	public static class ExtronMgpInputPorts
	{
		/// <summary>Port key for window <paramref name="window"/> (1-4): hdmiIn1..hdmiIn4</summary>
		public static string WindowPortKey(int window) { return "hdmiIn" + window; }

		/// <summary>Port key of the HDMI background input</summary>
		public const string BackgroundPortKey = "hdmiInBackground";

		/// <summary>Selector value of the background port (the manual's "99 = Background")</summary>
		public const int BackgroundSelector = 99;

		/// <summary>Adds hdmiIn1-4 and hdmiInBackground to <paramref name="parent"/>'s <see cref="IRoutingInputs.InputPorts"/></summary>
		public static void AddTo(IRoutingInputs parent)
		{
			const eRoutingSignalType type = eRoutingSignalType.Audio | eRoutingSignalType.Video;

			for (var window = 1; window <= ExtronMgpProtocol.WindowCount; window++)
				parent.InputPorts.Add(new RoutingInputPort(WindowPortKey(window), type, eRoutingPortConnectionType.Hdmi, window, parent));

			parent.InputPorts.Add(new RoutingInputPort(BackgroundPortKey, type, eRoutingPortConnectionType.Hdmi, BackgroundSelector, parent));
		}
	}
}
