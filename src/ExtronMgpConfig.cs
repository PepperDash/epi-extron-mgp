using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using PepperDash.Essentials.Core;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Extron MGP 641 device configuration object
	/// </summary>
	[ConfigSnippet("\"properties\":{\"control\":{}")]
	public class ExtronMgpPropertiesConfig
	{
		/// <summary>
		/// JSON control object
		/// </summary>
		/// <remarks>
		/// Supported methods: <c>ssh</c> (port 22023, the manual's default Ethernet method), <c>tcpIp</c> (Telnet; the unit's
		/// Telnet port 23 is disabled by default and must be enabled with <c>ESC Z 23 PMAP CR</c> first) and <c>com</c> (RS-232, 9600/8/N/1 by default).
		/// For <c>ssh</c> the login (<c>admin</c> and the unit serial number on a new unit) goes in <c>tcpSshProperties</c>.
		/// </remarks>
		/// <example>
		/// SSH (used for the README Config Example)
		/// <code>
		/// "control": {
		///		"method": "ssh",
		///		"tcpSshProperties": {
		///			"address": "172.22.0.101",
		///			"port": 22023,
		///			"username": "admin",
		///			"password": "unitSerialNumber",
		///			"autoReconnect": true,
		///			"autoReconnectIntervalMs": 10000
		///		}
		///	}
		/// </code>
		/// </example>
		/// <example>
		/// Telnet / TCP
		/// <code>
		/// "control": {
		///		"method": "tcpIp",
		///		"tcpSshProperties": {
		///			"address": "172.22.0.101",
		///			"port": 23,
		///			"autoReconnect": true,
		///			"autoReconnectIntervalMs": 10000
		///		}
		///	}
		/// </code>
		/// </example>
		/// <example>
		/// RS-232 on a processor COM port
		/// <code>
		/// "control": {
		///		"method": "com",
		///		"controlPortDevKey": "processor",
		///		"controlPortNumber": 1,
		///		"comParams": {
		///			"baudRate": 9600,
		///			"dataBits": 8,
		///			"stopBits": 1,
		///			"parity": "None",
		///			"protocol": "RS232",
		///			"hardwareHandshake": "None",
		///			"softwareHandshake": "None"
		///		}
		///	}
		/// </code>
		/// </example>
		[JsonProperty("control")]
		public EssentialsControlPropertiesConfig Control { get; set; }

		/// <summary>
		/// Password sent when the unit prompts for one over Telnet or RS-232 (SSH uses the control login instead).
		/// </summary>
		/// <example>
		/// <code>
		/// "properties": {
		///		"password": "unitSerialNumber"
		///	}
		/// </code>
		/// </example>
		[JsonProperty("password")]
		public string Password { get; set; }

		/// <summary>
		/// Poll time in milliseconds
		/// </summary>
		/// <example>
		/// <code>
		/// "properties": {
		///		"pollTimeMs": 30000
		/// }
		/// </code>
		/// </example>
		[JsonProperty("pollTimeMs")]
		public long PollTimeMs { get; set; }

		/// <summary>
		/// Warning timeout in milliseconds
		/// </summary>
		/// <example>
		/// <code>
		/// "properties": {
		///		"warningTimeoutMs": 180000
		/// }
		/// </code>
		/// </example>
		[JsonProperty("warningTimeoutMs")]
		public long WarningTimeoutMs { get; set; }

		/// <summary>
		/// Error timeout in milliseconds
		/// </summary>
		/// <example>
		/// <code>
		/// "properties": {
		///		"errorTimeoutMs": 300000
		/// }
		/// </code>
		/// </example>
		[JsonProperty("errorTimeoutMs")]
		public long ErrorTimeoutMs { get; set; }

		/// <summary>
		/// Layouts exposed to control, keyed by an id of your choosing.  Each maps to an MGP window preset (1-128).
		/// A missing <c>name</c> is read from the unit.  Source routing into the four windows is not configured here:
		/// the MGP exposes routing input ports (see README) that Essentials ties to an upstream switcher.
		/// </summary>
		/// <example>
		/// <code>
		/// "properties": {
		///		"layouts": {
		///			"full": { "preset": 1, "name": "Full Screen" },
		///			"quad": { "preset": 5, "name": "Quad" }
		///		}
		/// }
		/// </code>
		/// </example>
		[JsonProperty("layouts")]
		public Dictionary<string, ExtronMgpPresetConfig> Layouts { get; set; }

		/// <summary>Layouts as model definitions (null-safe)</summary>
		public IEnumerable<ExtronMgpPresetDefinition> LayoutDefinitions()
		{
			return ToDefinitions(Layouts);
		}

		public static IEnumerable<ExtronMgpPresetDefinition> ToDefinitions(Dictionary<string, ExtronMgpPresetConfig> source)
		{
			if (source == null) return Enumerable.Empty<ExtronMgpPresetDefinition>();
			return source
				.Where(kvp => kvp.Value != null)
				.Select(kvp => new ExtronMgpPresetDefinition { Id = kvp.Key, Preset = kvp.Value.Preset, Name = kvp.Value.Name })
				.ToList();
		}

		/// <summary>
		/// Constructor
		/// </summary>
		public ExtronMgpPropertiesConfig()
		{
			Layouts = new Dictionary<string, ExtronMgpPresetConfig>();
		}
	}

	/// <summary>
	/// A named reference to an MGP window preset (layout) slot
	/// </summary>
	public class ExtronMgpPresetConfig
	{
		/// <summary>
		/// Window preset slot on the unit (1-128)
		/// </summary>
		[JsonProperty("preset")]
		public int Preset { get; set; }

		/// <summary>
		/// Display name.  Optional; read from the unit when omitted.
		/// </summary>
		[JsonProperty("name")]
		public string Name { get; set; }
	}
}
