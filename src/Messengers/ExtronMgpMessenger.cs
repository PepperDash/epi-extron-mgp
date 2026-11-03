using System.Linq;
using Newtonsoft.Json.Linq;
using PepperDash.Essentials.AppServer.Messengers;

namespace PepperDash.Essentials.Plugins.Extron.Mgp
{
	/// <summary>
	/// Mobile Control messenger for the Extron MGP 641 at /device/{key}.
	/// </summary>
	/// <remarks>
	/// Actions (content is JSON):
	/// <list type="bullet">
	/// <item><c>/mgpStatus</c> - request the full state</item>
	/// <item><c>/recallLayout</c> - <c>"quad"</c> (layout id) or <c>{"layout":"quad"}</c></item>
	/// <item><c>/selectWindowInput</c> - <c>{"window":1,"input":"laptop"}</c></item>
	/// <item><c>/windowMute</c> - <c>{"window":1,"state":true}</c>; omit state to toggle</item>
	/// <item><c>/windowFreeze</c> - <c>{"window":1,"state":true}</c>; omit state to toggle</item>
	/// </list>
	/// The status action is not "/fullStatus": the device also implements ICommunicationMonitor and IDeviceInfoProvider,
	/// so Essentials attaches core messengers at the same path that each answer "/fullStatus" with their own payload.
	/// </remarks>
	public class ExtronMgpMessenger : MessengerBase
	{
		private readonly IExtronMgp device;

		/// <summary>
		/// Constructor
		/// </summary>
		public ExtronMgpMessenger(string key, string messagePath, IExtronMgp device)
			: base(key, messagePath, device)
		{
			this.device = device;
		}

		/// <inheritdoc />
		protected override void RegisterActions()
		{
			base.RegisterActions();

			AddAction("/mgpStatus", (id, content) => SendFullStatus(id));

			AddAction("/recallLayout", (id, content) =>
			{
				var layoutId = content is JObject ? (string)content["layout"] : content?.Type == JTokenType.String ? (string)content : null;
				device.RecallLayout(layoutId);
			});

			AddAction("/selectWindowInput", (id, content) =>
			{
				var window = (int?)content?["window"];
				var input = (string)content?["input"];
				if (window == null) return;
				device.SelectWindowInput(window.Value, input);
			});

			AddAction("/windowMute", (id, content) =>
			{
				var window = (int?)content?["window"];
				if (window == null) return;
				var state = (bool?)content["state"] ?? !device.Model.GetMute(window.Value);
				device.SetWindowMute(window.Value, state);
			});

			AddAction("/windowFreeze", (id, content) =>
			{
				var window = (int?)content?["window"];
				if (window == null) return;
				var state = (bool?)content["state"] ?? !device.Model.GetFreeze(window.Value);
				device.SetWindowFreeze(window.Value, state);
			});

			device.Model.StateChanged += (o, a) => SendFullStatus();
		}

		/// <summary>
		/// Sends the full state
		/// </summary>
		public void SendFullStatus(string id = null)
		{
			var layouts = device.Model.Layouts;
			var inputs = device.Model.Inputs;

			var message = new ExtronMgpStateMessage
			{
				Layouts = layouts.Select(l => new ExtronMgpPresetState { Id = l.Id, Name = l.Name, Preset = l.Preset }).ToArray(),
				CurrentLayoutPreset = device.Model.CurrentLayout,
				CurrentLayout = layouts.FirstOrDefault(l => l.Preset == device.Model.CurrentLayout)?.Id,
				Inputs = inputs.Select(i => new ExtronMgpPresetState { Id = i.Id, Name = i.Name, Preset = i.Preset }).ToArray(),
				Windows = Enumerable.Range(1, ExtronMgpProtocol.WindowCount).Select(w =>
				{
					var preset = device.Model.GetInputPreset(w);
					return new ExtronMgpWindowState
					{
						Window = w,
						InputPreset = preset,
						Input = preset == 0 ? null : inputs.FirstOrDefault(i => i.Preset == preset)?.Id,
						Muted = device.Model.GetMute(w),
						Frozen = device.Model.GetFreeze(w)
					};
				}).ToArray()
			};

			PostStatusMessage(message, id);
		}
	}
}
