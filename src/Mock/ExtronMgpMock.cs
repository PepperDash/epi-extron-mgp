using System.Collections.Generic;
using System.Linq;
using Crestron.SimplSharpPro.DeviceSupport;
using Newtonsoft.Json;
using PepperDash.Core.Logging;
using PepperDash.Essentials.Core;
using PepperDash.Essentials.Core.Bridges;
using PepperDash.Essentials.Core.DeviceInfo;
using PepperDash.Essentials.Core.DeviceTypeInterfaces;
using PepperDash.Essentials.Devices.Common;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Properties for <see cref="ExtronMgpMock"/>.  No <c>control</c> object is needed.
	/// </summary>
	public class ExtronMgpMockProperties
	{
		/// <summary>Layouts exposed to control (same shape as the real device)</summary>
		[JsonProperty("layouts")]
		public Dictionary<string, ExtronMgpPresetConfig> Layouts { get; set; }

		/// <summary>Start with the communication monitor in error, as if the unit were unreachable</summary>
		[JsonProperty("simulateOffline")]
		public bool SimulateOffline { get; set; }
	}

	/// <summary>
	/// In-memory stand-in for an MGP 641, for bench-testing rooms, Mobile Control UIs and EISC bridges with no hardware.
	/// Uses the same <see cref="ExtronMgpModel"/>, <see cref="ExtronMgpMessenger"/> and join map as the real device,
	/// so messenger paths, payloads and joins are identical.  Not for production use.
	/// </summary>
	/// <remarks>
	/// Starts with the first layout recalled and nothing muted or frozen.  Commands are dropped while the simulated comm status is not OK.  Front-panel changes and
	/// raw responses can be injected from the console with <c>devjson</c>, e.g.
	/// <c>{"deviceKey":"mgp-01","methodName":"SimulateLine","params":["Prst L 1*005"]}</c>,
	/// <c>SimulateLayout [5]</c>, <c>SimulateWindowMute [1,true]</c>,
	/// <c>SimulateWindowFreeze [1,true]</c> and <c>SetIsOnline [false]</c>.
	/// </remarks>
	public class ExtronMgpMock : EssentialsBridgeableDevice, IExtronMgp, IHasScreensWithLayouts, IRoutingInputs, IDeviceInfoProvider, ICommunicationMonitor
	{
		private readonly MockCommunicationMonitor monitor;

		/// <inheritdoc />
		public ExtronMgpModel Model { get; private set; }

		/// <inheritdoc />
		public ExtronMgpFeedbacks Feedbacks { get; private set; }

		/// <summary>Routing inputs for Essentials tie lines: hdmiIn1-4 (one per window) and hdmiInBackground</summary>
		public RoutingPortCollection<RoutingInputPort> InputPorts { get; private set; }

		/// <inheritdoc />
		public StatusMonitorBase CommunicationMonitor { get { return monitor; } }

		/// <summary>Online feedback through the bridge</summary>
		public BoolFeedback OnlineFeedback { get; private set; }

		/// <summary>Device name/IP feedback through the bridge</summary>
		public StringFeedback IpAddressFeedback { get; private set; }

		/// <inheritdoc />
		public DeviceInfo DeviceInfo { get; private set; }

		/// <inheritdoc />
		public event DeviceInfoChangeHandler DeviceInfoChanged;

		/// <summary>Constructor</summary>
		public ExtronMgpMock(string key, string name, ExtronMgpMockProperties config)
			: base(key, name)
		{
			Model = new ExtronMgpModel(ExtronMgpPropertiesConfig.ToDefinitions(config.Layouts));
			Feedbacks = new ExtronMgpFeedbacks(Model);
			screensView = new ExtronMgpScreens(this, name);

			InputPorts = new RoutingPortCollection<RoutingInputPort>();
			ExtronMgpInputPorts.AddTo(this);

			// Bench-friendly starting state
			var layouts = Model.Layouts;
			if (layouts.Count > 0) Model.SetCurrentLayout(layouts[0].Preset);

			Model.SetInfo("MGP-641-MOCK", "192.0.2.41", "00-05-A6-00-00-41", "1.00.0001");
			DeviceInfo = new DeviceInfo();
			Model.InfoChanged += (o, a) => PublishDeviceInfo();
			PublishDeviceInfo();

			monitor = new MockCommunicationMonitor(this);
			if (config.SimulateOffline) monitor.SetStatus(MonitorStatus.InError);

			OnlineFeedback = new BoolFeedback("online", () => monitor.IsOnline);
			monitor.StatusChange += (m, e) => OnlineFeedback.FireUpdate();
			IpAddressFeedback = new StringFeedback("ipAddress", () => DeviceInfo.IpAddress);
		}

		private bool IgnoreWhileOffline(string command)
		{
			if (monitor.Status == MonitorStatus.IsOk) return false;
			this.LogDebug("comm status is {status} - ignoring {command}", monitor.Status, command);
			return true;
		}

		#region IExtronMgp

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
			if (IgnoreWhileOffline("RecallLayout")) return;
			if (preset < 1 || preset > ExtronMgpProtocol.MaxLayoutPreset) return;
			this.LogInformation("MOCK recall layout preset {0}", preset);
			Model.SetCurrentLayout(preset);
		}

		/// <inheritdoc />
		public void SetWindowMute(int window, bool mute)
		{
			if (IgnoreWhileOffline("SetWindowMute") || !ExtronMgpModel.InRange(window)) return;
			Model.SetMute(window, mute);
		}

		/// <inheritdoc />
		public void SetWindowFreeze(int window, bool freeze)
		{
			if (IgnoreWhileOffline("SetWindowFreeze") || !ExtronMgpModel.InRange(window)) return;
			Model.SetFreeze(window, freeze);
		}

		#endregion

		#region Bench simulation (devjson)

		/// <summary>Reports the comm monitor OK (true) or in error (false); commands are dropped while in error</summary>
		public void SetIsOnline(bool online)
		{
			this.LogInformation("MOCK online {0}", online);
			monitor.SetStatus(online ? MonitorStatus.IsOk : MonitorStatus.InError);
		}

		/// <summary>Feeds a raw SIS response line through the real parser, as if the unit had sent it</summary>
		public void SimulateLine(string line)
		{
			var kind = Model.ProcessLine(line);
			this.LogInformation("MOCK line {0} -> {1}", line, kind);
		}

		/// <summary>Simulates a layout change made on the unit's front panel</summary>
		public void SimulateLayout(int preset) { Model.SetCurrentLayout(preset); }

		/// <summary>Simulates a window mute change from the front panel</summary>
		public void SimulateWindowMute(int window, bool muted) { Model.SetMute(window, muted); }

		/// <summary>Simulates a window freeze change from the front panel</summary>
		public void SimulateWindowFreeze(int window, bool frozen) { Model.SetFreeze(window, frozen); }

		#endregion

		#region IDeviceInfoProvider

		/// <inheritdoc />
		public void UpdateDeviceInfo() { PublishDeviceInfo(); }

		private void PublishDeviceInfo()
		{
			DeviceInfo.HostName = Model.HostName;
			DeviceInfo.IpAddress = Model.IpAddress;
			DeviceInfo.MacAddress = Model.MacAddress;
			DeviceInfo.FirmwareVersion = Model.FirmwareVersion;
			if (IpAddressFeedback != null) IpAddressFeedback.FireUpdate();

			var handler = DeviceInfoChanged;
			if (handler != null) handler(this, new DeviceInfoEventArgs(DeviceInfo));
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

		#region Mobile Control and bridge

		/// <inheritdoc />
		protected override void CreateMobileControlMessengers()
		{
			base.CreateMobileControlMessengers();

			var controller = DeviceManager.AllDevices.OfType<IMobileControl>().FirstOrDefault();
			if (controller == null) return;

			controller.AddDeviceMessenger(new ExtronMgpMessenger(Key + "-mgp-" + controller.Key, "/device/" + Key, this));
			screensView.RegisterMessengers(controller, this);
		}

		/// <inheritdoc />
		public override void LinkToApi(BasicTriList trilist, uint joinStart, string joinMapKey, EiscApiAdvanced bridge)
		{
			var joinMap = new ExtronMgpJoinMap(joinStart);
			bridge?.AddJoinMap(Key, joinMap);

			var customJoins = JoinMapHelper.TryGetJoinMapAdvancedForDevice(joinMapKey);
			if (customJoins != null) joinMap.SetCustomJoinData(customJoins);

			trilist.SetString(joinMap.DeviceName.JoinNumber, Name);
			OnlineFeedback.LinkInputSig(trilist.BooleanInput[joinMap.IsOnline.JoinNumber]);
			IpAddressFeedback.LinkInputSig(trilist.StringInput[joinMap.IpAddress.JoinNumber]);

			// Connect is a no-op on the mock; Status mirrors the monitor.
			trilist.UShortInput[joinMap.Status.JoinNumber].UShortValue = (ushort)monitor.Status;

			Feedbacks.Link(this, trilist, joinMap);
			OnlineFeedback.FireUpdate();
			IpAddressFeedback.FireUpdate();

			trilist.OnlineStatusChange += (o, a) =>
			{
				if (!a.DeviceOnLine) return;
				trilist.SetString(joinMap.DeviceName.JoinNumber, Name);
				OnlineFeedback.FireUpdate();
				IpAddressFeedback.FireUpdate();
				Feedbacks.FireAll();
			};
		}

		#endregion
	}
}
