using System;
using System.Collections.Generic;
using System.Linq;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>A configured layout or input: config key, MGP preset slot and optional configured name</summary>
	public class ExtronMgpPresetDefinition
	{
		/// <summary>Config key</summary>
		public string Id { get; set; }
		/// <summary>MGP preset slot (1-128)</summary>
		public int Preset { get; set; }
		/// <summary>Configured name; when empty the name read from the unit (or a fallback) is used</summary>
		public string Name { get; set; }
	}

	/// <summary>A selectable layout or input with its resolved display name</summary>
	public class ExtronMgpPresetItem
	{
		/// <summary>Config key</summary>
		public string Id { get; set; }
		/// <summary>MGP preset slot</summary>
		public int Preset { get; set; }
		/// <summary>Display name</summary>
		public string Name { get; set; }
	}

	/// <summary>What a received line turned out to be, for the transport to act on</summary>
	public enum ExtronMgpLineKind
	{
		/// <summary>Blank line</summary>
		Empty,
		/// <summary>Connect banner (copyright line)</summary>
		Banner,
		/// <summary>Unit is asking for a password</summary>
		PasswordPrompt,
		/// <summary>"Login Administrator" / "Login User"</summary>
		LoginAccepted,
		/// <summary>SIS error response (E01..E33); see <see cref="ExtronMgpModel.LastError"/></summary>
		Error,
		/// <summary>Updated layout, window, name or device-info state</summary>
		Handled,
		/// <summary>Not recognized (unsolicited event, echoed command or unknown format)</summary>
		Unhandled
	}

	/// <summary>
	/// Hardware-independent state of an MGP 641: current layout, per-window input/mute/freeze, preset names and
	/// device info, plus the SIS line parser that updates them.  Shared by the real device and the mock.
	/// Contains no Crestron or Essentials dependencies so it can be unit tested.
	/// </summary>
	public class ExtronMgpModel
	{
		private class Window
		{
			public int InputPreset;
			public bool Muted;
			public bool Frozen;
		}

		private readonly object sync = new object();
		private readonly List<ExtronMgpPresetDefinition> layoutDefs;
		private readonly List<ExtronMgpPresetDefinition> inputDefs;
		private readonly Dictionary<int, string> layoutNames = new Dictionary<int, string>();
		private readonly Dictionary<int, string> inputNames = new Dictionary<int, string>();
		private readonly Window[] windows = new Window[ExtronMgpProtocol.WindowCount + 1];
		private int currentLayout;
		private bool awaitingUnitName;

		/// <summary>Raised (outside the lock) after layout, window or name state changes</summary>
		public event EventHandler StateChanged;

		/// <summary>Raised (outside the lock) after hostname, IP, MAC or firmware changes</summary>
		public event EventHandler InfoChanged;

		/// <summary>Unit name (TCP/IP hostname)</summary>
		public string HostName { get; private set; }
		/// <summary>IP address</summary>
		public string IpAddress { get; set; }
		/// <summary>MAC address</summary>
		public string MacAddress { get; private set; }
		/// <summary>Firmware version</summary>
		public string FirmwareVersion { get; private set; }
		/// <summary>Last SIS error code received (E01..E33)</summary>
		public string LastError { get; private set; }

		/// <summary>Constructor</summary>
		public ExtronMgpModel(IEnumerable<ExtronMgpPresetDefinition> layouts, IEnumerable<ExtronMgpPresetDefinition> inputs)
		{
			layoutDefs = Clean(layouts);
			inputDefs = Clean(inputs);
			for (var i = 1; i <= ExtronMgpProtocol.WindowCount; i++) windows[i] = new Window();
		}

		private static List<ExtronMgpPresetDefinition> Clean(IEnumerable<ExtronMgpPresetDefinition> source)
		{
			return (source ?? Enumerable.Empty<ExtronMgpPresetDefinition>())
				.Where(d => d != null && d.Preset >= 1 && d.Preset <= ExtronMgpProtocol.MaxLayoutPreset)
				.OrderBy(d => d.Preset)
				.ToList();
		}

		#region Queries

		/// <summary>Layouts, ordered by preset number</summary>
		public IList<ExtronMgpPresetItem> Layouts { get { return Build(layoutDefs, layoutNames, "Layout"); } }

		/// <summary>Inputs selectable per window, ordered by preset number</summary>
		public IList<ExtronMgpPresetItem> Inputs { get { return Build(inputDefs, inputNames, "Input"); } }

		/// <summary>Configured layouts whose name must be read from the unit</summary>
		public IList<int> LayoutPresetsNeedingName { get { return layoutDefs.Where(d => string.IsNullOrEmpty(d.Name)).Select(d => d.Preset).ToList(); } }

		/// <summary>Configured inputs whose name must be read from the unit</summary>
		public IList<int> InputPresetsNeedingName { get { return inputDefs.Where(d => string.IsNullOrEmpty(d.Name)).Select(d => d.Preset).ToList(); } }

		private IList<ExtronMgpPresetItem> Build(List<ExtronMgpPresetDefinition> defs, Dictionary<int, string> unitNames, string fallbackPrefix)
		{
			lock (sync)
			{
				return defs.Select(d =>
				{
					string unitName;
					var name = !string.IsNullOrEmpty(d.Name)
						? d.Name
						: (unitNames.TryGetValue(d.Preset, out unitName) && !string.IsNullOrEmpty(unitName) ? unitName : fallbackPrefix + " " + d.Preset);
					return new ExtronMgpPresetItem { Id = d.Id, Preset = d.Preset, Name = name };
				}).ToList();
			}
		}

		/// <summary>Preset of the layout configured under <paramref name="id"/>; false if unknown</summary>
		public bool TryGetLayoutPreset(string id, out int preset)
		{
			return TryGet(layoutDefs, id, out preset);
		}

		/// <summary>Preset of the input configured under <paramref name="id"/>; false if unknown</summary>
		public bool TryGetInputPreset(string id, out int preset)
		{
			return TryGet(inputDefs, id, out preset);
		}

		private static bool TryGet(List<ExtronMgpPresetDefinition> defs, string id, out int preset)
		{
			var def = id == null ? null : defs.FirstOrDefault(d => d.Id == id);
			preset = def == null ? 0 : def.Preset;
			return def != null;
		}

		/// <summary>Current layout (window preset), 0 when unknown</summary>
		public int CurrentLayout { get { lock (sync) return currentLayout; } }

		/// <summary>Input preset last recalled to <paramref name="window"/> (1-4), 0 when unknown</summary>
		public int GetInputPreset(int window) { lock (sync) return InRange(window) ? windows[window].InputPreset : 0; }

		/// <summary>Mute state of <paramref name="window"/> (1-4)</summary>
		public bool GetMute(int window) { lock (sync) return InRange(window) && windows[window].Muted; }

		/// <summary>Freeze state of <paramref name="window"/> (1-4)</summary>
		public bool GetFreeze(int window) { lock (sync) return InRange(window) && windows[window].Frozen; }

		/// <summary>True for window numbers 1-4</summary>
		public static bool InRange(int window) { return window >= 1 && window <= ExtronMgpProtocol.WindowCount; }

		#endregion

		#region Updates

		/// <summary>Sets the current layout (also used for optimistic updates when a command is sent)</summary>
		public void SetCurrentLayout(int preset)
		{
			lock (sync)
			{
				if (currentLayout == preset) return;
				currentLayout = preset;
			}
			RaiseState();
		}

		/// <summary>Sets the input preset of a window</summary>
		public void SetInputPreset(int window, int preset)
		{
			lock (sync)
			{
				if (!InRange(window) || windows[window].InputPreset == preset) return;
				windows[window].InputPreset = preset;
			}
			RaiseState();
		}

		/// <summary>Sets mute for a window; window 0 means all windows</summary>
		public void SetMute(int window, bool muted)
		{
			SetFlag(window, w => w.Muted == muted, w => w.Muted = muted);
		}

		/// <summary>Sets freeze for a window; window 0 means all windows</summary>
		public void SetFreeze(int window, bool frozen)
		{
			SetFlag(window, w => w.Frozen == frozen, w => w.Frozen = frozen);
		}

		private void SetFlag(int window, Func<Window, bool> same, Action<Window> set)
		{
			var changed = false;
			lock (sync)
			{
				var targets = window == 0 ? Enumerable.Range(1, ExtronMgpProtocol.WindowCount) : new[] { window };
				foreach (var w in targets)
				{
					if (!InRange(w) || same(windows[w])) continue;
					set(windows[w]);
					changed = true;
				}
			}
			if (changed) RaiseState();
		}

		/// <summary>Sets a layout name as reported by the unit</summary>
		public void SetLayoutName(int preset, string name) { SetName(layoutNames, preset, name); }

		/// <summary>Sets an input preset name as reported by the unit</summary>
		public void SetInputName(int preset, string name) { SetName(inputNames, preset, name); }

		private void SetName(Dictionary<int, string> names, int preset, string name)
		{
			lock (sync)
			{
				string existing;
				if (names.TryGetValue(preset, out existing) && existing == name) return;
				names[preset] = name;
			}
			RaiseState();
		}

		/// <summary>Sets device info; null arguments leave the field unchanged</summary>
		public void SetInfo(string hostName = null, string ipAddress = null, string macAddress = null, string firmwareVersion = null)
		{
			var changed = false;
			lock (sync)
			{
				changed |= Assign(hostName, HostName, v => HostName = v);
				changed |= Assign(ipAddress, IpAddress, v => IpAddress = v);
				changed |= Assign(macAddress, MacAddress, v => MacAddress = v);
				changed |= Assign(firmwareVersion, FirmwareVersion, v => FirmwareVersion = v);
			}
			if (changed)
			{
				var handler = InfoChanged;
				if (handler != null) handler(this, EventArgs.Empty);
			}
		}

		private static bool Assign(string value, string current, Action<string> set)
		{
			if (value == null || value == current) return false;
			set(value);
			return true;
		}

		/// <summary>
		/// Call just before sending the unit-name query: some firmware replies with the bare name, so the next
		/// otherwise-unrecognized line is taken as the host name.
		/// </summary>
		public void ExpectUnitName() { lock (sync) awaitingUnitName = true; }

		private void RaiseState()
		{
			var handler = StateChanged;
			if (handler != null) handler(this, EventArgs.Empty);
		}

		#endregion

		#region Parsing

		/// <summary>
		/// Parses one received line and updates state.  Connect, password and login lines are only classified;
		/// the transport decides what to send in response.
		/// </summary>
		public ExtronMgpLineKind ProcessLine(string rawLine)
		{
			// Responses end CR/LF; also tolerate stray CR, LF, spaces or ESC around the text.
			var line = (rawLine ?? string.Empty).Trim('\r', '\n', ' ', '\x1B');
			if (line.Length == 0) return ExtronMgpLineKind.Empty;

			if (ExtronMgpProtocol.IsPasswordPrompt(line)) return ExtronMgpLineKind.PasswordPrompt;

			if (ExtronMgpProtocol.IsBanner(line) || ExtronMgpProtocol.IsLoginAccepted(line))
			{
				string fw;
				if (ExtronMgpProtocol.TryParseBannerFirmware(line, out fw) && string.IsNullOrEmpty(FirmwareVersion))
					SetInfo(firmwareVersion: fw);
				return ExtronMgpProtocol.IsBanner(line) ? ExtronMgpLineKind.Banner : ExtronMgpLineKind.LoginAccepted;
			}

			var error = ExtronMgpProtocol.ParseError(line);
			if (error != null)
			{
				LastError = error;
				return ExtronMgpLineKind.Error;
			}

			int a, b;
			bool flag;
			string text;

			if (ExtronMgpProtocol.TryParseMac(line, out text)) SetInfo(macAddress: text);
			else if (ExtronMgpProtocol.TryParseIpAddress(line, out text)) SetInfo(ipAddress: text);
			else if (ExtronMgpProtocol.TryParseFirmware(line, out text)) SetInfo(firmwareVersion: text);
			else if (ExtronMgpProtocol.TryParseTaggedUnitName(line, out text))
			{
				lock (sync) awaitingUnitName = false;
				SetInfo(hostName: text);
			}
			else if (ExtronMgpProtocol.TryParseInputPreset(line, out a, out b)) SetInputPreset(a, b);
			else if (ExtronMgpProtocol.TryParseLayout(line, out a)) SetCurrentLayout(a);
			else if (ExtronMgpProtocol.TryParsePresetName(line, out a, out b, out text))
			{
				if (a == 1) SetLayoutName(b, text); else SetInputName(b, text);
			}
			else if (ExtronMgpProtocol.TryParseMute(line, out a, out flag)) SetMute(a, flag);
			else if (ExtronMgpProtocol.TryParseFreeze(line, out a, out flag)) SetFreeze(a, flag);
			else
			{
				bool expecting;
				lock (sync) { expecting = awaitingUnitName; awaitingUnitName = false; }
				if (!expecting) return ExtronMgpLineKind.Unhandled;
				SetInfo(hostName: line);
			}

			return ExtronMgpLineKind.Handled;
		}

		#endregion
	}
}
