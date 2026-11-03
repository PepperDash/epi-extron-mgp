using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Extron MGP 641 bridge join map
	/// </summary>
	public class ExtronMgpJoinMap : JoinMapBaseAdvanced
	{
		[JoinName("IsOnline")]
		public JoinDataComplete IsOnline = new JoinDataComplete(
			new JoinData { JoinNumber = 1, JoinSpan = 1 },
			new JoinMetadata
			{
				Description = "Is Online",
				JoinCapabilities = eJoinCapabilities.ToSIMPL,
				JoinType = eJoinType.Digital
			});

		[JoinName("Connect")]
		public JoinDataComplete Connect = new JoinDataComplete(
			new JoinData { JoinNumber = 2, JoinSpan = 1 },
			new JoinMetadata
			{
				Description = "Connect (Held)/Disconnect (Release) & corresponding feedback",
				JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
				JoinType = eJoinType.Digital
			});

		[JoinName("WindowMuteOn")]
		public JoinDataComplete WindowMuteOn = new JoinDataComplete(
			new JoinData { JoinNumber = 11, JoinSpan = 4 },
			new JoinMetadata
			{
				Description = "Window 1-4 mute on (pulse) & feedback",
				JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
				JoinType = eJoinType.Digital
			});

		[JoinName("WindowMuteOff")]
		public JoinDataComplete WindowMuteOff = new JoinDataComplete(
			new JoinData { JoinNumber = 21, JoinSpan = 4 },
			new JoinMetadata
			{
				Description = "Window 1-4 mute off (pulse) & feedback",
				JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
				JoinType = eJoinType.Digital
			});

		[JoinName("WindowFreezeOn")]
		public JoinDataComplete WindowFreezeOn = new JoinDataComplete(
			new JoinData { JoinNumber = 31, JoinSpan = 4 },
			new JoinMetadata
			{
				Description = "Window 1-4 freeze on (pulse) & feedback",
				JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
				JoinType = eJoinType.Digital
			});

		[JoinName("WindowFreezeOff")]
		public JoinDataComplete WindowFreezeOff = new JoinDataComplete(
			new JoinData { JoinNumber = 41, JoinSpan = 4 },
			new JoinMetadata
			{
				Description = "Window 1-4 freeze off (pulse) & feedback",
				JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
				JoinType = eJoinType.Digital
			});

		[JoinName("Status")]
		public JoinDataComplete Status = new JoinDataComplete(
			new JoinData { JoinNumber = 1, JoinSpan = 1 },
			new JoinMetadata
			{
				Description = "Socket status",
				JoinCapabilities = eJoinCapabilities.ToSIMPL,
				JoinType = eJoinType.Analog
			});

		[JoinName("LayoutSelect")]
		public JoinDataComplete LayoutSelect = new JoinDataComplete(
			new JoinData { JoinNumber = 11, JoinSpan = 1 },
			new JoinMetadata
			{
				Description = "Layout (MGP window preset 1-128) select & feedback",
				JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
				JoinType = eJoinType.Analog
			});

		[JoinName("WindowInputSelect")]
		public JoinDataComplete WindowInputSelect = new JoinDataComplete(
			new JoinData { JoinNumber = 21, JoinSpan = 4 },
			new JoinMetadata
			{
				Description = "Window 1-4 input (MGP input preset 1-128) select & feedback",
				JoinCapabilities = eJoinCapabilities.ToFromSIMPL,
				JoinType = eJoinType.Analog
			});

		[JoinName("DeviceName")]
		public JoinDataComplete DeviceName = new JoinDataComplete(
			new JoinData { JoinNumber = 1, JoinSpan = 1 },
			new JoinMetadata
			{
				Description = "Device name",
				JoinCapabilities = eJoinCapabilities.ToSIMPL,
				JoinType = eJoinType.Serial
			});

		[JoinName("IpAddress")]
		public JoinDataComplete IpAddress = new JoinDataComplete(
			new JoinData { JoinNumber = 2, JoinSpan = 1 },
			new JoinMetadata
			{
				Description = "IP address",
				JoinCapabilities = eJoinCapabilities.ToSIMPL,
				JoinType = eJoinType.Serial
			});

		/// <summary>
		/// Constructor
		/// </summary>
		public ExtronMgpJoinMap(uint joinStart)
			: base(joinStart, typeof(ExtronMgpJoinMap))
		{
		}
	}
}
