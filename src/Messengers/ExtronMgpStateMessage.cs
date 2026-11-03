using Newtonsoft.Json;
using PepperDash.Essentials.AppServer.Messengers;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Full state of the MGP sent to the React app.  Posted to /device/{key} in response to /mgpStatus and on every change.
	/// </summary>
	public class ExtronMgpStateMessage : DeviceStateMessageBase
	{
		/// <summary>Layouts the app can recall</summary>
		[JsonProperty("layouts")]
		public ExtronMgpPresetState[] Layouts { get; set; }

		/// <summary>Id of the current layout, or null when it matches no configured layout (or is unknown)</summary>
		[JsonProperty("currentLayout", NullValueHandling = NullValueHandling.Include)]
		public string CurrentLayout { get; set; }

		/// <summary>MGP window preset number of the current layout, 0 when unknown</summary>
		[JsonProperty("currentLayoutPreset")]
		public int CurrentLayoutPreset { get; set; }

		/// <summary>Inputs the app can select per window</summary>
		[JsonProperty("inputs")]
		public ExtronMgpPresetState[] Inputs { get; set; }

		/// <summary>The four windows</summary>
		[JsonProperty("windows")]
		public ExtronMgpWindowState[] Windows { get; set; }
	}

	/// <summary>A selectable layout or input</summary>
	public class ExtronMgpPresetState
	{
		/// <summary>Config key; pass this back to select it</summary>
		[JsonProperty("id")]
		public string Id { get; set; }

		/// <summary>Display name</summary>
		[JsonProperty("name")]
		public string Name { get; set; }

		/// <summary>MGP preset slot</summary>
		[JsonProperty("preset")]
		public int Preset { get; set; }
	}

	/// <summary>State of one window</summary>
	public class ExtronMgpWindowState
	{
		/// <summary>Window number, 1-4</summary>
		[JsonProperty("window")]
		public int Window { get; set; }

		/// <summary>Id of the selected input, null when unknown (e.g. after a reconnect: the unit cannot report it)</summary>
		[JsonProperty("input", NullValueHandling = NullValueHandling.Include)]
		public string Input { get; set; }

		/// <summary>Input preset number last recalled to the window, 0 when unknown</summary>
		[JsonProperty("inputPreset")]
		public int InputPreset { get; set; }

		/// <summary>Window muted</summary>
		[JsonProperty("muted")]
		public bool Muted { get; set; }

		/// <summary>Window frozen</summary>
		[JsonProperty("frozen")]
		public bool Frozen { get; set; }
	}
}
