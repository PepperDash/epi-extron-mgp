using System.Text.RegularExpressions;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// SIS (Simple Instruction Set) command builders and response parsers for the MGP 641.
	/// </summary>
	/// <remarks>
	/// Source: MGP 641 User Guide (68-2396-01 Rev C), "Remote Configuration and Control".  The manual's tables
	/// are typeset with spaces that are not part of the commands; only <c>•</c> means a real space.
	/// <c>ESC</c> is 0x1B, <c>}</c> is a carriage return.  All responses end CR/LF.
	/// The plugin runs the unit in verbose mode 3, so queries come back tagged and unsolicited changes are pushed.
	/// Response parsing is deliberately lenient (optional spaces, zero padding) because the manual's response
	/// column is ambiguous in places; every line that does not match is logged by the device.
	/// </remarks>
	public static class ExtronMgpProtocol
	{
		/// <summary>Escape character that prefixes SIS "E" commands</summary>
		public const string Esc = "\x1B";

		/// <summary>Terminator for SIS "E" commands</summary>
		public const string Cr = "\r";

		/// <summary>Number of windows (and HDMI inputs) on the MGP 641</summary>
		public const int WindowCount = 4;

		/// <summary>Window presets (layouts) available on the unit</summary>
		public const int MaxLayoutPreset = 128;

		/// <summary>Input presets available on the unit</summary>
		public const int MaxInputPreset = 128;

		#region Commands

		/// <summary>Turns SIS-over-SSH echo off for this connection (control systems should do this on connect)</summary>
		public static string EchoOff() { return Esc + "0ECHO" + Cr; }

		/// <summary>Verbose mode 3: unsolicited feedback plus tagged query responses</summary>
		public static string VerboseMode3() { return Esc + "3CV" + Cr; }

		/// <summary>Recall window preset (layout) <paramref name="preset"/></summary>
		public static string RecallLayout(int preset) { return "1*" + preset + "."; }

		/// <summary>View last recalled window preset</summary>
		public static string QueryCurrentLayout() { return Esc + "L1PRST" + Cr; }

		/// <summary>View the name of window preset <paramref name="preset"/></summary>
		public static string QueryLayoutName(int preset) { return Esc + "1*" + preset + "PNAM" + Cr; }

		/// <summary>Recall input preset <paramref name="preset"/> to <paramref name="window"/></summary>
		public static string RecallInputPreset(int window, int preset) { return "2*" + window + "*" + preset + "."; }

		/// <summary>View the name of input preset <paramref name="preset"/></summary>
		public static string QueryInputPresetName(int preset) { return Esc + "2*" + preset + "PNAM" + Cr; }

		/// <summary>Mute (blank) or unmute <paramref name="window"/></summary>
		public static string SetWindowMute(int window, bool mute) { return window + "*" + (mute ? 1 : 0) + "B"; }

		/// <summary>View mute status of <paramref name="window"/></summary>
		public static string QueryWindowMute(int window) { return window + "B"; }

		/// <summary>Freeze or unfreeze <paramref name="window"/></summary>
		public static string SetWindowFreeze(int window, bool freeze) { return window + "*" + (freeze ? 1 : 0) + "F"; }

		/// <summary>View freeze status of <paramref name="window"/></summary>
		public static string QueryWindowFreeze(int window) { return window + "F"; }

		/// <summary>View the current IP address</summary>
		public static string QueryIpAddress() { return Esc + "CI" + Cr; }

		/// <summary>View the MAC address</summary>
		public static string QueryMacAddress() { return Esc + "CH" + Cr; }

		/// <summary>View the unit name (TCP/IP hostname)</summary>
		public static string QueryUnitName() { return Esc + "CN" + Cr; }

		/// <summary>View firmware version with build number (n.nn.nnnn)</summary>
		public static string QueryFirmwareBuild() { return "*Q"; }

		#endregion

		#region Responses

		private static readonly Regex LayoutRecalledRegex = new Regex(@"^(?:1\s*Rpr\s*|Rpr\s*1\s*\*?\s*)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex CurrentLayoutRegex = new Regex(@"^Prst\s*L\s*1\s*\*\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex InputPresetRecalledRegex = new Regex(@"^(?:2\s*Rpr\s*|Rpr\s*2\s*\*?\s*)(\d+)\s*\*\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex PresetNameRegex = new Regex(@"^Pnam\s*([12])\s*\*\s*(\d+)\s*,\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex MuteRegex = new Regex(@"^Vmt\s*(\d+)\s*\*\s*(\d)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex FreezeRegex = new Regex(@"^Frz\s*(\d+)\s*\*\s*(\d)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex ErrorRegex = new Regex(@"^E(\d\d)$", RegexOptions.Compiled);

		private static readonly Regex Ipv4Regex = new Regex(@"^\d{1,3}(\.\d{1,3}){3}$", RegexOptions.Compiled);
		private static readonly Regex MacRegex = new Regex(@"^(?:Iph\s*[\u2022 ]*)?([0-9A-Fa-f]{2}(?:[-:][0-9A-Fa-f]{2}){5})$", RegexOptions.Compiled);
		private static readonly Regex FirmwareRegex = new Regex(@"^(?:Ver\s*01\s*\*\s*|Bld\s*)(\d+\.\d+(?:\.\d+)?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex UnitNameRegex = new Regex(@"^Ipn[\u2022 ]+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex BannerFirmwareRegex = new Regex(@",\s*V(\d+\.\d+)\s*,", RegexOptions.Compiled);

		/// <summary>Bare IPv4 address response to the IP address query</summary>
		public static bool TryParseIpAddress(string line, out string ip)
		{
			ip = line;
			return Ipv4Regex.IsMatch(line);
		}

		/// <summary>MAC address, tagged (Iph) or bare</summary>
		public static bool TryParseMac(string line, out string mac)
		{
			var m = MacRegex.Match(line);
			mac = m.Success ? m.Groups[1].Value : null;
			return m.Success;
		}

		/// <summary>Firmware version from a Ver01/Bld response</summary>
		public static bool TryParseFirmware(string line, out string version)
		{
			var m = FirmwareRegex.Match(line);
			version = m.Success ? m.Groups[1].Value : null;
			return m.Success;
		}

		/// <summary>Firmware version from the connect banner</summary>
		public static bool TryParseBannerFirmware(string line, out string version)
		{
			var m = BannerFirmwareRegex.Match(line);
			version = m.Success ? m.Groups[1].Value : null;
			return m.Success;
		}

		/// <summary>Tagged unit name response (Ipn)</summary>
		public static bool TryParseTaggedUnitName(string line, out string name)
		{
			var m = UnitNameRegex.Match(line);
			name = m.Success ? m.Groups[1].Value.Trim() : null;
			return m.Success;
		}

		/// <summary>True when the line is the connect banner, e.g. "(c) Copyright 2026, Extron Electronics, MGP 641, V1.00, 60-1574-01"</summary>
		public static bool IsBanner(string line)
		{
			return line != null && line.IndexOf("Copyright", System.StringComparison.OrdinalIgnoreCase) >= 0 && line.IndexOf("Extron", System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>True when the unit is asking for a password</summary>
		public static bool IsPasswordPrompt(string line)
		{
			return line != null && line.IndexOf("Password", System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>True for "Login Administrator" / "Login User"</summary>
		public static bool IsLoginAccepted(string line)
		{
			return line != null && line.StartsWith("Login", System.StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>Parses an SIS error response (E01..E33); returns the code or null</summary>
		public static string ParseError(string line)
		{
			var m = ErrorRegex.Match(line);
			return m.Success ? "E" + m.Groups[1].Value : null;
		}

		/// <summary>Layout (window preset) recalled or reported as the last recalled</summary>
		public static bool TryParseLayout(string line, out int preset)
		{
			var m = LayoutRecalledRegex.Match(line);
			if (!m.Success) m = CurrentLayoutRegex.Match(line);
			preset = 0;
			return m.Success && int.TryParse(m.Groups[1].Value, out preset);
		}

		/// <summary>Input preset recalled to a window</summary>
		public static bool TryParseInputPreset(string line, out int window, out int preset)
		{
			window = 0;
			preset = 0;
			var m = InputPresetRecalledRegex.Match(line);
			return m.Success && int.TryParse(m.Groups[1].Value, out window) && int.TryParse(m.Groups[2].Value, out preset);
		}

		/// <summary>Preset name; <paramref name="kind"/> is 1 for window presets (layouts), 2 for input presets</summary>
		public static bool TryParsePresetName(string line, out int kind, out int preset, out string name)
		{
			kind = 0;
			preset = 0;
			name = null;
			var m = PresetNameRegex.Match(line);
			if (!m.Success) return false;
			name = m.Groups[3].Value.Trim();
			return int.TryParse(m.Groups[1].Value, out kind) && int.TryParse(m.Groups[2].Value, out preset);
		}

		/// <summary>Window mute state; 0 = unmuted, 1 = muted</summary>
		public static bool TryParseMute(string line, out int window, out bool muted)
		{
			return TryParseWindowFlag(MuteRegex, line, out window, out muted);
		}

		/// <summary>Window freeze state</summary>
		public static bool TryParseFreeze(string line, out int window, out bool frozen)
		{
			return TryParseWindowFlag(FreezeRegex, line, out window, out frozen);
		}

		private static bool TryParseWindowFlag(Regex regex, string line, out int window, out bool value)
		{
			window = 0;
			value = false;
			var m = regex.Match(line);
			if (!m.Success || !int.TryParse(m.Groups[1].Value, out window)) return false;
			value = m.Groups[2].Value != "0";
			return true;
		}

		#endregion
	}
}
