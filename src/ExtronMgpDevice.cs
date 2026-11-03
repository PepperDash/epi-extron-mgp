using System;
using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharpPro.DeviceSupport;
using PepperDash.Core;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.DeviceInfo;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;
using PepperDash.Essentials.Core.Queues;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Extron MGP 641 four-window processor.  Controlled over SIS via SSH (22023), Telnet/TCP (23) or RS-232.
	/// Exposes layout (window preset) selection and per-window input (input preset) selection, plus window mute and freeze.
	/// </summary>
	public class ExtronMgpDevice : EssentialsBridgeableDevice, IExtronMgp, IHasScreensWithLayouts, IDeviceInfoProvider, ICommunicationMonitor
	{
		private const string CommsDelimiter = "\r\n";

		private readonly ExtronMgpPropertiesConfig config;
		private readonly GenericQueue receiveQueue;
		private readonly IBasicCommunication comms;
		private readonly GenericCommunicationMonitor commsMonitor;
		private readonly CommunicationGather commsGather;
		private readonly bool isSsh;
		private volatile bool sessionInitialized;

		/// <inheritdoc />
		public ExtronMgpModel Model { get; private set; }

		/// <inheritdoc />
		public ExtronMgpFeedbacks Feedbacks { get; private set; }

		/// <summary>
		/// Poll-driven online monitor.  Essentials auto-attaches the core communication monitor messenger.
		/// </summary>
		public StatusMonitorBase CommunicationMonitor { get { return commsMonitor; } }

		/// <summary>Connect feedback through the bridge</summary>
		public BoolFeedback ConnectFeedback { get; private set; }

		/// <summary>Online feedback through the bridge</summary>
		public BoolFeedback OnlineFeedback { get; private set; }

		/// <summary>Socket status feedback through the bridge</summary>
		public IntFeedback StatusFeedback { get; private set; }

		/// <summary>Device IP address through the bridge</summary>
		public StringFeedback IpAddressFeedback { get; private set; }

		/// <summary>
		/// Connects/disconnects the comms of the plugin device
		/// </summary>
		public bool Connect
		{
			get { return comms.IsConnected; }
			set
			{
				if (value)
				{
					comms.Connect();
					commsMonitor.Start();
				}
				else
				{
					comms.Disconnect();
					commsMonitor.Stop();
				}
			}
		}

		/// <summary>
		/// Constructor
		/// </summary>
		public ExtronMgpDevice(string key, string name, ExtronMgpPropertiesConfig config, IBasicCommunication comms)
			: base(key, name)
		{
			this.LogInformation("Constructing new {0} instance", name);

			this.config = config;
			this.comms = comms;
			isSsh = config.Control != null && config.Control.Method == eControlMethod.Ssh;

			Model = new ExtronMgpModel(config.LayoutDefinitions(), config.InputDefinitions());
			Feedbacks = new ExtronMgpFeedbacks(Model);
			screensView = new ExtronMgpScreens(this, name);

			receiveQueue = new GenericQueue(key + "-rxqueue");

			ConnectFeedback = new BoolFeedback("connect", () => Connect);
			OnlineFeedback = new BoolFeedback("online", () => commsMonitor.IsOnline);
			StatusFeedback = new IntFeedback("status", () => (int)commsMonitor.Status);
			IpAddressFeedback = new StringFeedback("ipAddress", () => DeviceInfo.IpAddress);

			DeviceInfo = new DeviceInfo
			{
				IpAddress = config.Control?.TcpSshProperties?.Address ?? string.Empty
			};
			Model.IpAddress = DeviceInfo.IpAddress;
			Model.InfoChanged += (o, a) => PublishDeviceInfo();

			var poll = config.PollTimeMs > 0 ? config.PollTimeMs : 30000;
			var warn = config.WarningTimeoutMs > 0 ? config.WarningTimeoutMs : 180000;
			var error = config.ErrorTimeoutMs > 0 ? config.ErrorTimeoutMs : 300000;
			commsMonitor = new GenericCommunicationMonitor(this, comms, poll, warn, error, Poll);
			commsMonitor.StatusChange += (o, a) =>
			{
				// A unit that dropped off (power cycle, cable pulled) forgets verbose mode; re-initialize when it returns.
				if (!commsMonitor.IsOnline) sessionInitialized = false;
				OnlineFeedback.FireUpdate();
				StatusFeedback.FireUpdate();
			};

			var socket = comms as ISocketStatus;
			if (socket != null)
			{
				socket.ConnectionChange += socket_ConnectionChange;
			}

			commsGather = new CommunicationGather(comms, CommsDelimiter);
			commsGather.LineReceived += (s, a) => receiveQueue.Enqueue(new ProcessStringMessage(a.Text, ProcessFeedbackMessage));
		}

		/// <inheritdoc />
		protected override bool CustomActivate()
		{
			Connect = true;

			// RS-232 has no connect event or banner (the banner is only sent after a power cycle), so start the init sequence now.
			if (!(comms is ISocketStatus)) InitializeSession();

			return base.CustomActivate();
		}

		#region Commands

		/// <inheritdoc />
		public void RecallLayout(string layoutId)
		{
			int preset;
			if (!Model.TryGetLayoutPreset(layoutId, out preset))
			{
				this.LogWarning("Unknown layout id {0}", layoutId);
				return;
			}

			RecallLayoutPreset(preset);
		}

		/// <inheritdoc />
		public void RecallLayoutPreset(int preset)
		{
			if (preset < 1 || preset > ExtronMgpProtocol.MaxLayoutPreset)
			{
				this.LogWarning("Layout preset {0} out of range", preset);
				return;
			}

			SendText(ExtronMgpProtocol.RecallLayout(preset));
			Model.SetCurrentLayout(preset);

			if (config.ReapplyInputsAfterLayout)
			{
				for (var w = 1; w <= ExtronMgpProtocol.WindowCount; w++)
				{
					var inputPreset = Model.GetInputPreset(w);
					if (inputPreset > 0) SendText(ExtronMgpProtocol.RecallInputPreset(w, inputPreset));
				}
			}
		}

		/// <inheritdoc />
		public void SelectWindowInput(int window, string inputId)
		{
			int preset;
			if (!Model.TryGetInputPreset(inputId, out preset))
			{
				this.LogWarning("Unknown input id {0}", inputId);
				return;
			}

			SelectWindowInputPreset(window, preset);
		}

		/// <inheritdoc />
		public void SelectWindowInputPreset(int window, int preset)
		{
			if (!ExtronMgpModel.InRange(window) || preset < 1 || preset > ExtronMgpProtocol.MaxInputPreset)
			{
				this.LogWarning("Window {0} / input preset {1} out of range", window, preset);
				return;
			}

			SendText(ExtronMgpProtocol.RecallInputPreset(window, preset));
			Model.SetInputPreset(window, preset);
		}

		/// <inheritdoc />
		public void SetWindowMute(int window, bool mute)
		{
			if (!ExtronMgpModel.InRange(window)) return;
			SendText(ExtronMgpProtocol.SetWindowMute(window, mute));
			Model.SetMute(window, mute);
		}

		/// <inheritdoc />
		public void SetWindowFreeze(int window, bool freeze)
		{
			if (!ExtronMgpModel.InRange(window)) return;
			SendText(ExtronMgpProtocol.SetWindowFreeze(window, freeze));
			Model.SetFreeze(window, freeze);
		}

		/// <summary>
		/// Sends raw SIS text to the unit; useful with DEVJSON for testing.  Escape-prefixed commands must include
		/// the ESC (0x1B) and CR themselves.
		/// </summary>
		public void SendText(string text)
		{
			if (string.IsNullOrEmpty(text)) return;
			this.LogVerbose("Sending {0}", Printable(text));
			comms.SendText(text);
		}

		/// <summary>
		/// Poll: set the session up if it is not yet, otherwise ask for the last recalled layout (cheap).
		/// </summary>
		public void Poll()
		{
			if (!sessionInitialized)
			{
				InitializeSession();
				return;
			}

			SendText(ExtronMgpProtocol.QueryCurrentLayout());
		}

		private void InitializeSession()
		{
			sessionInitialized = true;
			this.LogDebug("Initializing MGP session");

			if (isSsh) SendText(ExtronMgpProtocol.EchoOff());
			SendText(ExtronMgpProtocol.VerboseMode3());
			SendText(ExtronMgpProtocol.QueryCurrentLayout());

			// Only configured presets without a configured name are queried, not all 128 slots.
			foreach (var p in Model.LayoutPresetsNeedingName) SendText(ExtronMgpProtocol.QueryLayoutName(p));
			foreach (var p in Model.InputPresetsNeedingName) SendText(ExtronMgpProtocol.QueryInputPresetName(p));

			for (var w = 1; w <= ExtronMgpProtocol.WindowCount; w++)
			{
				SendText(ExtronMgpProtocol.QueryWindowMute(w));
				SendText(ExtronMgpProtocol.QueryWindowFreeze(w));
			}

			QueryDeviceInfo();
		}

		private void QueryDeviceInfo()
		{
			SendText(ExtronMgpProtocol.QueryIpAddress());
			SendText(ExtronMgpProtocol.QueryMacAddress());
			SendText(ExtronMgpProtocol.QueryFirmwareBuild());
			// The unit name reply may be untagged, so it is queried last (see ExtronMgpModel.ExpectUnitName).
			Model.ExpectUnitName();
			SendText(ExtronMgpProtocol.QueryUnitName());
		}

		#endregion

		#region Feedback

		private void socket_ConnectionChange(object sender, GenericSocketStatusChageEventArgs args)
		{
			if (!comms.IsConnected) sessionInitialized = false;

			ConnectFeedback.FireUpdate();
			StatusFeedback.FireUpdate();
		}

		private void ProcessFeedbackMessage(string message)
		{
			try
			{
				switch (Model.ProcessLine(message))
				{
					case ExtronMgpLineKind.PasswordPrompt:
						if (!string.IsNullOrEmpty(config.Password))
							comms.SendText(config.Password + ExtronMgpProtocol.Cr);
						else
							this.LogWarning("Unit requested a password but no 'password' is configured");
						break;

					case ExtronMgpLineKind.Banner:
						// The banner arrives on connect (and after an RS-232 power cycle).  Over a password-protected
						// Telnet link it comes before the prompt, so wait for "Login ..." in that case.
						if (!string.IsNullOrEmpty(config.Password) && !isSsh && comms is ISocketStatus) break;
						InitializeSession();
						break;

					case ExtronMgpLineKind.LoginAccepted:
						InitializeSession();
						break;

					case ExtronMgpLineKind.Error:
						this.LogWarning("Unit returned error {0}", Model.LastError);
						break;

					case ExtronMgpLineKind.Unhandled:
						// Unsolicited events (Reconfig, HplgO, HdcpI/O, IN00...) and echoed commands land here too.
						this.LogDebug("Unhandled line: {0}", Printable(message));
						break;
				}
			}
			catch (Exception ex)
			{
				this.LogError("Error processing line {0}: {1}", Printable(message), ex.Message);
			}
		}

		private static string Printable(string s)
		{
			return (s ?? string.Empty).Replace("\x1B", "<ESC>").Replace("\r", "<CR>").Replace("\n", "<LF>");
		}

		#endregion

		#region IDeviceInfoProvider

		/// <summary>
		/// Network and identity information for the device
		/// </summary>
		public DeviceInfo DeviceInfo { get; private set; }

		/// <summary>
		/// Raised when the DeviceInfo changes
		/// </summary>
		public event DeviceInfoChangeHandler DeviceInfoChanged;

		/// <summary>
		/// Refreshes the DeviceInfo from the unit (when the session is up) and notifies subscribers
		/// </summary>
		public void UpdateDeviceInfo()
		{
			if (sessionInitialized) QueryDeviceInfo();
			PublishDeviceInfo();
		}

		private void PublishDeviceInfo()
		{
			DeviceInfo.HostName = Model.HostName;
			DeviceInfo.IpAddress = Model.IpAddress;
			DeviceInfo.MacAddress = Model.MacAddress;
			DeviceInfo.FirmwareVersion = Model.FirmwareVersion;
			IpAddressFeedback.FireUpdate();

			var handler = DeviceInfoChanged;
			if (handler != null)
			{
				handler(this, new DeviceInfoEventArgs(DeviceInfo));
			}
		}

		#endregion

		#region IHasScreensWithLayouts

		private ExtronMgpScreens screensView;

		/// <inheritdoc />
		public Dictionary<uint, ScreenInfo> Screens { get { return screensView.Screens; } }

		/// <inheritdoc />
		public void ApplyLayout(uint screenId, uint layoutIndex)
		{
			screensView.ApplyLayout(screenId, layoutIndex);
		}

		#endregion

		#region Mobile Control

		/// <inheritdoc />
		protected override void CreateMobileControlMessengers()
		{
			base.CreateMobileControlMessengers();

			var controller = DeviceManager.AllDevices.OfType<IMobileControl>().FirstOrDefault();
			if (controller == null) return;

			controller.AddDeviceMessenger(new ExtronMgpMessenger(Key + "-mgp-" + controller.Key, "/device/" + Key, this));
			screensView.RegisterMessengers(controller, this);
		}

		#endregion

		#region Bridge

		/// <summary>
		/// Links the plugin device to the EISC bridge
		/// </summary>
		public override void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
		{
			var joinMap = new ExtronMgpJoinMap(joinStart);

			bridge?.AddJoinMap(Key, joinMap);

			var customJoins = JoinMapHelper.TryGetJoinMapAdvancedForDevice(joinMapKey);
			if (customJoins != null)
			{
				joinMap.SetCustomJoinData(customJoins);
			}

			this.LogDebug("Linking to Trilist {id}", trilist.ID.ToString("X"));
			this.LogInformation("Linking to Bridge Type {type}", GetType().Name);

			trilist.SetString(joinMap.DeviceName.JoinNumber, Name);

			trilist.SetBoolSigAction(joinMap.Connect.JoinNumber, sig => Connect = sig);
			ConnectFeedback.LinkInputSig(trilist.BooleanInput[joinMap.Connect.JoinNumber]);
			StatusFeedback.LinkInputSig(trilist.UShortInput[joinMap.Status.JoinNumber]);
			OnlineFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);
			IpAddressFeedback.LinkInputSig(trilist.StringInput[joinMap.IpAddress.JoinNumber]);

			Feedbacks.Link(this, trilist, joinMap);

			UpdateFeedbacks();

			trilist.OnlineStatusChange += (o, a) =>
			{
				if (!a.DeviceOnLine) return;

				trilist.SetString(joinMap.DeviceName.JoinNumber, Name);
				UpdateFeedbacks();
			};
		}

		private void UpdateFeedbacks()
		{
			ConnectFeedback.FireUpdate();
			OnlineFeedback.FireUpdate();
			StatusFeedback.FireUpdate();
			IpAddressFeedback.FireUpdate();
			Feedbacks.FireAll();
		}

		#endregion
	}
}
